using AutoPilotAgent.Core.Interfaces;

namespace AutoPilotAgent.Core.Services;

public sealed class ArmState : IArmState
{
    private readonly object _gate = new();
    private bool _isArmed;

    public bool IsArmed
    {
        get
        {
            lock (_gate)
            {
                return _isArmed;
            }
        }
    }

    public void Arm()
    {
        lock (_gate)
        {
            _isArmed = true;
        }
    }

    public void Disarm()
    {
        lock (_gate)
        {
            _isArmed = false;
        }
    }
}
