using AutoPilotAgent.Core.Models;

namespace AutoPilotAgent.Core.Interfaces;

public interface IPlanService
{
    Task<PlanModel> GeneratePlanAsync(string goal);
}
