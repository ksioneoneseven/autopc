namespace AutoPilotAgent.Core.Models;

public sealed class InteractionState
{
    public List<InteractionRecord> RecentActions { get; } = new();
    public int MaxRecords { get; init; } = 10;

    public void RecordAction(string actionType, string details, bool success)
    {
        RecentActions.Add(new InteractionRecord
        {
            Timestamp = DateTime.UtcNow,
            ActionType = actionType,
            Details = details,
            Success = success
        });

        while (RecentActions.Count > MaxRecords)
        {
            RecentActions.RemoveAt(0);
        }
    }

    public bool HasRepeatedAction(string actionType, string details, int threshold = 2)
    {
        var count = RecentActions.Count(r => 
            r.ActionType == actionType && 
            r.Details.Contains(details, StringComparison.OrdinalIgnoreCase));
        return count >= threshold;
    }

    public string GetSummary()
    {
        if (RecentActions.Count == 0)
        {
            return "No actions taken yet.";
        }

        var lines = RecentActions.Select((r, i) => 
            $"{i + 1}. {r.ActionType}: {r.Details} [{(r.Success ? "OK" : "FAILED")}]");
        
        return string.Join("\n", lines);
    }
}

public sealed class InteractionRecord
{
    public DateTime Timestamp { get; init; }
    public required string ActionType { get; init; }
    public required string Details { get; init; }
    public bool Success { get; init; }
}
