namespace AutoPilotAgent.Core.Models;

public sealed class PlanStep
{
    public required int Id { get; init; }
    public required string Description { get; init; }
    public required RiskLevel RiskLevel { get; init; }
    public required bool RequiresConfirmation { get; init; }
    public required string Validation { get; init; }
}
