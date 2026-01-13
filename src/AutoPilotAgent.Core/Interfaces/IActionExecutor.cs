using AutoPilotAgent.Core.Models;

namespace AutoPilotAgent.Core.Interfaces;

public interface IActionExecutor
{
    Task<ExecutionResult> ExecuteAsync(ActionModel action);
}
