using AutoPilotAgent.Core.Interfaces;
using AutoPilotAgent.Core.Models;

namespace AutoPilotAgent.Core.Services;

public sealed class StepValidationService : IStepValidationService
{
    public bool Validate(string validationExpression, ObservationModel observation)
    {
        if (string.IsNullOrWhiteSpace(validationExpression))
        {
            return observation.LastActionSuccess == true;
        }

        var v = validationExpression.Trim();

        if (v.Equals("last_action_success", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("last_action_success == true", StringComparison.OrdinalIgnoreCase))
        {
            return observation.LastActionSuccess == true;
        }

        const string windowContainsPrefix = "active_window_title contains ";
        if (v.StartsWith(windowContainsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var needle = v.Substring(windowContainsPrefix.Length).Trim().Trim('"');
            return (observation.ActiveWindowTitle ?? string.Empty)
                .Contains(needle, StringComparison.OrdinalIgnoreCase);
        }

        return observation.LastActionSuccess == true;
    }
}
