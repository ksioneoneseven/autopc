using System.Windows.Automation;

namespace AutoPilotAgent.Automation.UIA;

public sealed class UIAutomationHelper
{
    public AutomationElement? FindByAutomationIdOrName(string? automationId, string? name)
    {
        var root = AutomationElement.RootElement;
        if (root is null)
        {
            return null;
        }

        var conditions = new List<Condition>();

        if (!string.IsNullOrWhiteSpace(automationId))
        {
            conditions.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            conditions.Add(new PropertyCondition(AutomationElement.NameProperty, name));
        }

        if (conditions.Count == 0)
        {
            return null;
        }

        Condition condition = conditions.Count == 1 ? conditions[0] : new OrCondition(conditions.ToArray());
        return root.FindFirst(TreeScope.Descendants, condition);
    }

    public bool TryInvoke(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var p) && p is InvokePattern invoke)
        {
            invoke.Invoke();
            return true;
        }

        return false;
    }

    public bool IsPasswordElement(AutomationElement element)
    {
        try
        {
            return element.Current.IsPassword;
        }
        catch
        {
            return false;
        }
    }
}
