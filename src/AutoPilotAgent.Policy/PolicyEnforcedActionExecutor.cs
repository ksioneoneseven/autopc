using System.Text.Json;
using AutoPilotAgent.Automation.Services;
using AutoPilotAgent.Core.Interfaces;
using AutoPilotAgent.Core.Models;

namespace AutoPilotAgent.Policy;

public sealed class PolicyEnforcedActionExecutor : IActionExecutor
{
    private readonly ActionExecutor _inner;
    private readonly IPolicyEngine _policy;
    private readonly IObservationService _observation;
    private readonly Func<bool> _isAutoApprove;

    private readonly object _gate = new();
    private string? _lastFocusedProcess;
    private string? _lastFocusedTitle;

    public PolicyEnforcedActionExecutor(ActionExecutor inner, IPolicyEngine policy, IObservationService observation, Func<bool> isAutoApprove)
    {
        _inner = inner;
        _policy = policy;
        _observation = observation;
        _isAutoApprove = isAutoApprove;
    }

    public async Task<ExecutionResult> ExecuteAsync(ActionModel action)
    {
        if (action.ActionType == ActionType.FocusWindow)
        {
            var targetProcess = GetString(action.Parameters, "process");
            if (!string.IsNullOrWhiteSpace(targetProcess) && !_policy.IsAllowedProcess(targetProcess))
            {
                return new ExecutionResult { Success = false, ErrorMessage = $"Target process not allowed: {targetProcess}" };
            }

            var res = await _inner.ExecuteAsync(action);
            if (res.Success)
            {
                lock (_gate)
                {
                    _lastFocusedProcess = targetProcess;
                    _lastFocusedTitle = GetString(action.Parameters, "title");
                }
            }

            return res;
        }

        var obs = _observation.Observe();
        var isOurOwnUi = string.Equals(obs.ActiveProcess, "AutoPilotAgent.UI", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(obs.ActiveProcess, "AutoPilotAgent", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(obs.ActiveProcess) && !_policy.IsAllowedProcess(obs.ActiveProcess))
        {
            // Allow user-confirmed or auto-approved global Win-key hotkeys even if our UI is foreground.
            if (IsWinHotkeyAllowed(action))
            {
                return await _inner.ExecuteAsync(action);
            }

            // Allow NavigateUrl to execute - it launches a browser which will take focus
            if (action.ActionType == ActionType.NavigateUrl)
            {
                return await _inner.ExecuteAsync(action);
            }

            // If our own UI is foreground, allow all actions (the agent needs to work)
            if (isOurOwnUi && _isAutoApprove())
            {
                return await _inner.ExecuteAsync(action);
            }

            // Try to refocus the last allowlisted target (common case: UI is still foreground).
            var (p, t) = GetLastFocusTarget();

            if (!string.IsNullOrWhiteSpace(p) && _policy.IsAllowedProcess(p))
            {
                var focusAction = BuildFocusAction(p!, t);
                var focusResult = await _inner.ExecuteAsync(focusAction);

                if (focusResult.Success)
                {
                    // Re-check foreground after focusing.
                    obs = _observation.Observe();
                    if (!string.IsNullOrWhiteSpace(obs.ActiveProcess) && _policy.IsAllowedProcess(obs.ActiveProcess))
                    {
                        return await _inner.ExecuteAsync(action);
                    }
                }
            }

            return new ExecutionResult { Success = false, ErrorMessage = $"Foreground process not allowed: {obs.ActiveProcess}" };
        }

        return await _inner.ExecuteAsync(action);
    }

    private bool IsWinHotkeyAllowed(ActionModel action)
    {
        if (action.ActionType != ActionType.Hotkey)
        {
            return false;
        }

        // Allow if user-confirmed OR auto-approve is enabled.
        var approved = action.RequiresConfirmation || _isAutoApprove();
        if (!approved)
        {
            return false;
        }

        if (!action.Parameters.TryGetProperty("keys", out var keysEl) || keysEl.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var k in keysEl.EnumerateArray())
        {
            if (k.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var s = k.GetString();
            if (string.Equals(s, "WIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "LWIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "RWIN", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private (string? process, string? title) GetLastFocusTarget()
    {
        lock (_gate)
        {
            return (_lastFocusedProcess, _lastFocusedTitle);
        }
    }

    private static ActionModel BuildFocusAction(string process, string? title)
    {
        var safeTitle = title ?? string.Empty;
        var json = $"{{\"title\":\"{EscapeJson(safeTitle)}\",\"process\":\"{EscapeJson(process)}\"}}";
        return new ActionModel
        {
            ActionType = ActionType.FocusWindow,
            RequiresConfirmation = false,
            ExpectedResult = $"Focus {process}",
            Parameters = JsonDocument.Parse(json).RootElement
        };
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string? GetString(JsonElement obj, string name)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
        {
            return el.GetString();
        }

        return null;
    }
}
