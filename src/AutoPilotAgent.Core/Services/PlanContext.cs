using AutoPilotAgent.Core.Interfaces;
using AutoPilotAgent.Core.Models;

namespace AutoPilotAgent.Core.Services;

public sealed class PlanContext : IPlanContext
{
    private readonly object _gate = new();
    private PlanModel? _currentPlan;

    public PlanModel? CurrentPlan
    {
        get
        {
            lock (_gate)
            {
                return _currentPlan;
            }
        }
        set
        {
            lock (_gate)
            {
                _currentPlan = value;
            }
        }
    }
}
