using System.Text.Json;

namespace AutoPilotAgent.Core.Models;

public sealed class ActionModel
{
    public required ActionType ActionType { get; init; }
    public JsonElement Parameters { get; init; }
    public required bool RequiresConfirmation { get; init; }
    public string? ExpectedResult { get; init; }
}
