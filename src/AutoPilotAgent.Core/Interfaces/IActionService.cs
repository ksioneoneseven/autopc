using AutoPilotAgent.Core.Models;

namespace AutoPilotAgent.Core.Interfaces;

public interface IActionService
{
    Task<ActionModel> GetNextActionAsync(PlanStep step, ObservationModel observation, string? goalContext = null, InteractionState? interactionState = null);
    Task<StepCompletionResult> CheckStepCompletionAsync(PlanStep step, ObservationModel observation, string? goalContext = null);
}

public sealed class StepCompletionResult
{
    public bool IsComplete { get; init; }
    public string? Reason { get; init; }
    public string? SuggestedNextAction { get; init; }
}
