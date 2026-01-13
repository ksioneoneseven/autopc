using AutoPilotAgent.Core.Models;

namespace AutoPilotAgent.Core.Interfaces;

public interface IObservationService
{
    ObservationModel Observe(ExecutionResult? lastResult = null);
}
