using System.Windows;
using AutoPilotAgent.Core.Interfaces;
using AutoPilotAgent.Core.Models;
using AutoPilotAgent.UI.Views;

namespace AutoPilotAgent.UI.Services;

public sealed class WpfUserConfirmationService : IUserConfirmationService
{
    private readonly UserPreferences _preferences;

    public WpfUserConfirmationService(UserPreferences preferences)
    {
        _preferences = preferences;
    }

    public async Task<bool> RequestUserConfirmationAsync(ActionModel action, CancellationToken cancellationToken)
    {
        if (_preferences.AutoApprove)
        {
            return true;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return false;
        }

        return await dispatcher.InvokeAsync(() =>
        {
            var dialog = new ConfirmationDialog(action);
            dialog.Topmost = true;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dialog.Activate();
            return dialog.ShowDialog() == true;
        });
    }

    public async Task RequestManualTakeoverAsync(string reason, CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            MessageBox.Show(reason, "Manual Takeover Required", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }
}
