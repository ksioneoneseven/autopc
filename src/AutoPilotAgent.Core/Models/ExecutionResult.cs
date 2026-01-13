namespace AutoPilotAgent.Core.Models;

public sealed class ExecutionResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Details { get; init; }
}
