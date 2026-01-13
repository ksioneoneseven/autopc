namespace AutoPilotAgent.OpenAI;

public sealed class OpenAIOptions
{
    public string BaseUrl { get; init; } = "https://api.openai.com/v1/responses";
    public string Model { get; init; } = "gpt-5.2-chat-latest";
    public int MaxOutputTokens { get; init; } = 800;
    public int TimeoutSeconds { get; init; } = 60;
}
