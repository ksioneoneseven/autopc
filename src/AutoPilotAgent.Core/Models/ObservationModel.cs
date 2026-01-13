namespace AutoPilotAgent.Core.Models;

public sealed class ObservationModel
{
    public string? ActiveWindowTitle { get; init; }
    public string? ActiveProcess { get; init; }
    public bool? LastActionSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ScreenshotDataUrl { get; init; }
}
