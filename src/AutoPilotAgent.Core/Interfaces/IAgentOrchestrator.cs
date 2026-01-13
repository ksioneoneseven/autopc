namespace AutoPilotAgent.Core.Interfaces;

public interface IAgentOrchestrator
{
    Task RunGoalAsync(string goalText);
    void Stop();
}
