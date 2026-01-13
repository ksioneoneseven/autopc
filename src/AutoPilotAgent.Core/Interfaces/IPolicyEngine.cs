using AutoPilotAgent.Core.Models;

namespace AutoPilotAgent.Core.Interfaces;

public interface IPolicyEngine
{
    bool RequiresConfirmation(ActionModel action);
    bool IsAllowedProcess(string processName);
}
