namespace AutoPilotAgent.Core.Models;

public sealed class PlanModel
{
    public required string Goal { get; init; }
    public List<string> ClarifyingQuestions { get; init; } = new();
    public List<string> RequiredApps { get; init; } = new();
    public List<PlanStep> Steps { get; init; } = new();
}
