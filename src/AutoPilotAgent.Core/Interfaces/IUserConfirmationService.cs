using AutoPilotAgent.Core.Models;

namespace AutoPilotAgent.Core.Interfaces;

public interface IUserConfirmationService
{
    Task<bool> RequestUserConfirmationAsync(ActionModel action, CancellationToken cancellationToken);
    Task RequestManualTakeoverAsync(string reason, CancellationToken cancellationToken);
}
