using AutoPilotAgent.Core.Interfaces;
using AutoPilotAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace AutoPilotAgent.Core.Services;

public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly IArmState _armState;
    private readonly IPlanContext _planContext;
    private readonly IPlanService _planService;
    private readonly IActionService _actionService;
    private readonly IActionExecutor _executor;
    private readonly IPolicyEngine _policy;
    private readonly IUserConfirmationService _confirmation;
    private readonly IObservationService _observationService;
    private readonly IStepValidationService _validationService;
    private readonly ILogger<AgentOrchestrator> _logger;

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;

    public AgentState State { get; private set; } = AgentState.Idle;

    public AgentOrchestrator(
        IArmState armState,
        IPlanContext planContext,
        IPlanService planService,
        IActionService actionService,
        IActionExecutor executor,
        IPolicyEngine policy,
        IUserConfirmationService confirmation,
        IObservationService observationService,
        IStepValidationService validationService,
        ILogger<AgentOrchestrator> logger)
    {
        _armState = armState;
        _planContext = planContext;
        _planService = planService;
        _actionService = actionService;
        _executor = executor;
        _policy = policy;
        _confirmation = confirmation;
        _observationService = observationService;
        _validationService = validationService;
        _logger = logger;
    }

    public async Task RunGoalAsync(string goalText)
    {
        CancellationToken token;
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
        }

        try
        {
            State = AgentState.Planning;
            _logger.LogInformation("Planning goal: {Goal}", goalText);

            var plan = _planContext.CurrentPlan;
            if (plan is null || !string.Equals(plan.Goal, goalText, StringComparison.OrdinalIgnoreCase))
            {
                plan = await _planService.GeneratePlanAsync(goalText);
                _planContext.CurrentPlan = plan;
            }

            State = AgentState.Ready;

            if (!_armState.IsArmed)
            {
                _logger.LogWarning("Agent is disarmed; refusing to execute plan.");
                throw new InvalidOperationException("Agent must be Armed before running a plan.");
            }

            State = AgentState.Executing;

            foreach (var step in plan.Steps.OrderBy(s => s.Id))
            {
                token.ThrowIfCancellationRequested();

                _logger.LogInformation("Starting step {StepId}: {Description}", step.Id, step.Description);
                var stepComplete = false;
                var actionCount = 0;
                const int maxActionsPerStep = 15;
                var interactionState = new InteractionState();

                while (!stepComplete && actionCount < maxActionsPerStep)
                {
                    token.ThrowIfCancellationRequested();
                    actionCount++;

                    // Get fresh observation with screenshot
                    var observation = _observationService.Observe();

                    var action = await _actionService.GetNextActionAsync(step, observation, goalText, interactionState);

                    // If the model signals "done", mark step complete immediately
                    if (action.ActionType == ActionType.Done)
                    {
                        _logger.LogInformation("Step {StepId}: Model signaled task is DONE", step.Id);
                        stepComplete = true;
                        break;
                    }

                    // Prevent looping: if we've done this exact action 3+ times, skip to next step
                    var actionSummary = action.ActionType.ToString();
                    if (action.Parameters.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        if (action.Parameters.TryGetProperty("text", out var textEl))
                            actionSummary += $":{textEl.GetString()}";
                        else if (action.Parameters.TryGetProperty("command", out var cmdEl))
                            actionSummary += $":{cmdEl.GetString()}";
                        else if (action.Parameters.TryGetProperty("process", out var procEl))
                            actionSummary += $":{procEl.GetString()}";
                    }
                    
                    if (interactionState.HasRepeatedAction(action.ActionType.ToString(), actionSummary, 3))
                    {
                        _logger.LogWarning("Step {StepId}: Detected repeated action {Action}, moving to next step", step.Id, actionSummary);
                        break;
                    }

                    var requiresConfirmation = _policy.RequiresConfirmation(action)
                        || step.RequiresConfirmation
                        || step.RiskLevel == RiskLevel.High;

                    if (requiresConfirmation)
                    {
                        State = AgentState.WaitingConfirmation;
                        var ok = await _confirmation.RequestUserConfirmationAsync(action, token);
                        if (!ok)
                        {
                            throw new OperationCanceledException("User declined confirmation.");
                        }
                        State = AgentState.Executing;
                    }

                    var result = await ExecuteWithRetriesAsync(action, token);
                    
                    // Record the action in interaction state
                    var actionDetails = result.Details ?? action.ActionType.ToString();
                    interactionState.RecordAction(action.ActionType.ToString(), actionDetails, result.Success);
                    
                    if (!result.Success)
                    {
                        _logger.LogWarning("Action failed: {Error}. Continuing to check completion.", result.ErrorMessage);
                    }
                    else
                    {
                        _logger.LogInformation("Action succeeded: {Details}", result.Details);
                    }

                    // Wait for UI to update - longer for app-launching actions
                    var isAppLaunch = action.ActionType is ActionType.WinRun or ActionType.NavigateUrl;
                    await Task.Delay(isAppLaunch ? 1500 : 300, token);

                    // Get fresh screenshot and verify step completion via vision
                    var postActionObservation = _observationService.Observe(result);

                    _logger.LogInformation("Checking step {StepId} completion via vision (action {Count}/{Max})...",
                        step.Id, actionCount, maxActionsPerStep);

                    var completionResult = await _actionService.CheckStepCompletionAsync(step, postActionObservation, goalText);

                    if (completionResult.IsComplete)
                    {
                        stepComplete = true;
                        _logger.LogInformation("Step {StepId} verified complete: {Reason}", step.Id, completionResult.Reason);
                    }
                    else
                    {
                        _logger.LogInformation("Step {StepId} not complete: {Reason}. Suggested: {Suggested}",
                            step.Id, completionResult.Reason, completionResult.SuggestedNextAction);
                    }
                }

                if (!stepComplete)
                {
                    _logger.LogWarning("Step {StepId} did not complete after {Max} actions. Moving to next step.",
                        step.Id, maxActionsPerStep);
                }
            }

            State = AgentState.Completed;
            _logger.LogInformation("Goal completed.");
        }
        catch (OperationCanceledException)
        {
            State = AgentState.Stopped;
            _logger.LogWarning("Execution stopped.");
            throw;
        }
        catch (Exception ex)
        {
            State = AgentState.Failed;
            _logger.LogError(ex, "Execution failed.");
            await _confirmation.RequestManualTakeoverAsync(ex.Message, token);
            throw;
        }
        finally
        {
            lock (_gate)
            {
                _cts?.Dispose();
                _cts = null;
            }

            if (State is AgentState.Executing or AgentState.WaitingConfirmation or AgentState.Ready or AgentState.Planning)
            {
                State = AgentState.Idle;
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
        }
    }

    private async Task<ExecutionResult> ExecuteWithRetriesAsync(ActionModel action, CancellationToken token)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                _logger.LogInformation("Executing action {ActionType} attempt {Attempt}/{Max}", action.ActionType, attempt, maxAttempts);
                var result = await _executor.ExecuteAsync(action);

                if (result.Success)
                {
                    return result;
                }

                _logger.LogWarning("Action failed: {Error}", result.ErrorMessage);
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "Action threw on attempt {Attempt}/{Max}", attempt, maxAttempts);
            }

            if (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), token);
            }
        }

        return new ExecutionResult
        {
            Success = false,
            ErrorMessage = "Action failed after retries"
        };
    }
}
