namespace AutoPilotAgent.Core.Models;

public enum AgentState
{
    Idle,
    Planning,
    Ready,
    Executing,
    WaitingConfirmation,
    Completed,
    Failed,
    Stopped
}
