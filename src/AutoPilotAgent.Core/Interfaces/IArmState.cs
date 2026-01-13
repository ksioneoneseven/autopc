namespace AutoPilotAgent.Core.Interfaces;

public interface IArmState
{
    bool IsArmed { get; }
    void Arm();
    void Disarm();
}
