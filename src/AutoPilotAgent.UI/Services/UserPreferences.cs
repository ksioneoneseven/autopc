namespace AutoPilotAgent.UI.Services;

public sealed class UserPreferences
{
    private readonly object _gate = new();
    private bool _autoApprove = true;

    public bool AutoApprove
    {
        get
        {
            lock (_gate)
            {
                return _autoApprove;
            }
        }
        set
        {
            lock (_gate)
            {
                _autoApprove = value;
            }
        }
    }
}
