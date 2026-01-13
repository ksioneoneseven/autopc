using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoPilotAgent.OpenAI;

public sealed class PlanDto
{
    [JsonPropertyName("goal")]
    public required string Goal { get; init; }

    [JsonPropertyName("clarifying_questions")]
    public List<string>? ClarifyingQuestions { get; init; }

    [JsonPropertyName("required_apps")]
    public List<string>? RequiredApps { get; init; }

    [JsonPropertyName("steps")]
    public required List<PlanStepDto> Steps { get; init; }
}

public sealed class PlanStepDto
{
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("risk_level")]
    public required string RiskLevel { get; init; }

    [JsonPropertyName("requires_confirmation")]
    public required bool RequiresConfirmation { get; init; }

    [JsonPropertyName("validation")]
    public required string Validation { get; init; }
}

public sealed class ActionDto
{
    [JsonPropertyName("action_type")]
    public required string ActionType { get; init; }

    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; init; }

    [JsonPropertyName("requires_confirmation")]
    public required bool RequiresConfirmation { get; init; }

    [JsonPropertyName("expected_result")]
    public string? ExpectedResult { get; init; }
}
