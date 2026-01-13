using System.Text.Json;
using System.Windows;
using AutoPilotAgent.Core.Models;

namespace AutoPilotAgent.UI.Views;

public sealed partial class ConfirmationDialog : Window
{
    public ConfirmationDialog(ActionModel action)
    {
        InitializeComponent();
        Owner = Application.Current?.MainWindow;
        DataContext = new ConfirmationDialogViewModel(action);
    }

    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private sealed class ConfirmationDialogViewModel
    {
        public ConfirmationDialogViewModel(ActionModel action)
        {
            ActionType = action.ActionType.ToString();
            ExpectedResult = action.ExpectedResult ?? string.Empty;
            ParametersJson = action.Parameters.ValueKind == JsonValueKind.Undefined ? "{}" : action.Parameters.GetRawText();
        }

        public string ActionType { get; }
        public string ExpectedResult { get; }
        public string ParametersJson { get; }
    }
}
