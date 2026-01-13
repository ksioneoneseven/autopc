using AutoPilotAgent.Core.Models;

namespace AutoPilotAgent.Core.Interfaces;

public interface IPlanContext
{
    PlanModel? CurrentPlan { get; set; }
}
