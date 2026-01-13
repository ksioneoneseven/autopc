namespace AutoPilotAgent.Logging;

public sealed class LogEntry
{
    public required DateTime TimestampUtc { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
}
