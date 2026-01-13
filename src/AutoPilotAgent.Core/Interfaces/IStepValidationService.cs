using AutoPilotAgent.Core.Models;

namespace AutoPilotAgent.Core.Interfaces;

public interface IStepValidationService
{
    bool Validate(string validationExpression, ObservationModel observation);
}
