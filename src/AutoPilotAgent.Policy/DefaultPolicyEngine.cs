using AutoPilotAgent.Core.Interfaces;
using AutoPilotAgent.Core.Models;

namespace AutoPilotAgent.Policy;

public sealed class DefaultPolicyEngine : IPolicyEngine
{
    private readonly HashSet<string> _allowedProcesses;

    public DefaultPolicyEngine(IEnumerable<string>? allowedProcesses = null)
    {
        _allowedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (allowedProcesses is not null)
        {
            foreach (var p in allowedProcesses)
            {
                if (!string.IsNullOrWhiteSpace(p))
                {
                    _allowedProcesses.Add(p.Trim());
                }
            }
        }

        if (_allowedProcesses.Count == 0)
        {
            // Default allowlist for common apps.
            _allowedProcesses.Add("notepad");
            _allowedProcesses.Add("explorer");
            _allowedProcesses.Add("mspaint");
            // Browsers
            _allowedProcesses.Add("firefox");
            _allowedProcesses.Add("chrome");
            _allowedProcesses.Add("msedge");
            _allowedProcesses.Add("iexplore");
            // Productivity
            _allowedProcesses.Add("WINWORD");
            _allowedProcesses.Add("EXCEL");
            _allowedProcesses.Add("POWERPNT");
            _allowedProcesses.Add("Code");
            _allowedProcesses.Add("notepad++");
            _allowedProcesses.Add("calc");
            _allowedProcesses.Add("cmd");
            _allowedProcesses.Add("powershell");
            _allowedProcesses.Add("WindowsTerminal");
        }
    }

    public bool RequiresConfirmation(ActionModel action)
    {
        if (action.RequiresConfirmation)
        {
            return true;
        }

        return action.ActionType switch
        {
            ActionType.ClickCoordinates => true,
            _ => false
        };
    }

    public bool IsAllowedProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        return _allowedProcesses.Contains(processName.Trim());
    }
}
