using System.Collections.ObjectModel;
using System.Text.Json;
using AutoPilotAgent.Core.Interfaces;
using AutoPilotAgent.Core.Models;
using AutoPilotAgent.Logging;
using AutoPilotAgent.Storage.Secrets;
using AutoPilotAgent.Storage.Sqlite;
using AutoPilotAgent.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AutoPilotAgent.UI.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IArmState _armState;
    private readonly IPlanContext _planContext;
    private readonly IPlanService _planService;
    private readonly IAgentOrchestrator _orchestrator;
    private readonly InMemoryLogSink _logSink;
    private readonly ApiKeyStore _apiKeyStore;
    private readonly SqliteRepository _repo;
    private readonly UserPreferences _preferences;
    private readonly ILogger<MainViewModel> _logger;

    public ObservableCollection<PlanStep> Steps { get; } = new();
    public ObservableCollection<string> RequiredApps { get; } = new();
    public ObservableCollection<string> ClarifyingQuestions { get; } = new();
    public ObservableCollection<LogEntry> Logs { get; } = new();

    [ObservableProperty]
    private string _goalText = "Open Notepad, type 'Hello from AutoPilot Agent', and save the file to Documents as test.txt";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isArmed;

    [ObservableProperty]
    private string _statusText = "Idle";

    [ObservableProperty]
    private string _apiKeyText = string.Empty;

    [ObservableProperty]
    private bool _autoApprove;

    public MainViewModel(
        IArmState armState,
        IPlanContext planContext,
        IPlanService planService,
        IAgentOrchestrator orchestrator,
        InMemoryLogSink logSink,
        ApiKeyStore apiKeyStore,
        SqliteRepository repo,
        UserPreferences preferences,
        ILogger<MainViewModel> logger)
    {
        _armState = armState;
        _planContext = planContext;
        _planService = planService;
        _orchestrator = orchestrator;
        _logSink = logSink;
        _apiKeyStore = apiKeyStore;
        _repo = repo;
        _preferences = preferences;
        _logger = logger;

        IsArmed = _armState.IsArmed;
        AutoApprove = _preferences.AutoApprove;

        foreach (var entry in _logSink.Snapshot())
        {
            Logs.Add(entry);
        }

        _logSink.LogEmitted += OnLogEmitted;

        if (_planContext.CurrentPlan is not null)
        {
            SetPlan(_planContext.CurrentPlan);
        }
    }

    partial void OnAutoApproveChanged(bool value)
    {
        _preferences.AutoApprove = value;
        _logger.LogInformation("Auto-approve confirmations set to {Value}", value);
    }

    private void OnLogEmitted(LogEntry entry)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() =>
        {
            Logs.Add(entry);
            while (Logs.Count > 2000)
            {
                Logs.RemoveAt(0);
            }
        });
    }

    [RelayCommand]
    private void ToggleArm()
    {
        if (_armState.IsArmed)
        {
            _armState.Disarm();
            IsArmed = false;
            StatusText = "Disarmed";
        }
        else
        {
            _armState.Arm();
            IsArmed = true;
            StatusText = "Armed";
        }
    }

    [RelayCommand]
    private async Task GeneratePlanAsync()
    {
        IsBusy = true;
        StatusText = "Planning";

        try
        {
            var plan = await _planService.GeneratePlanAsync(GoalText);
            _planContext.CurrentPlan = plan;
            SetPlan(plan);

            var planJson = JsonSerializer.Serialize(plan);
            _repo.InsertPlan(plan.Goal, planJson);

            _logger.LogInformation("Plan ready with {Count} steps", plan.Steps.Count);
            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plan generation failed");
            StatusText = "Plan failed";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunPlanAsync()
    {
        IsBusy = true;
        StatusText = "Executing";

        try
        {
            await Task.Run(async () => await _orchestrator.RunGoalAsync(GoalText));
            StatusText = "Completed";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Stopped";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execution failed");
            StatusText = "Failed";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _orchestrator.Stop();
        StatusText = "Stopping";
    }

    [RelayCommand]
    private void SaveApiKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyText))
        {
            return;
        }

        _apiKeyStore.SaveApiKey(ApiKeyText);
        ApiKeyText = string.Empty;
        _logger.LogInformation("API key saved.");
    }

    private void SetPlan(PlanModel plan)
    {
        Steps.Clear();
        RequiredApps.Clear();
        ClarifyingQuestions.Clear();

        foreach (var s in plan.Steps.OrderBy(s => s.Id))
        {
            Steps.Add(s);
        }

        foreach (var a in plan.RequiredApps)
        {
            RequiredApps.Add(a);
        }

        foreach (var q in plan.ClarifyingQuestions)
        {
            ClarifyingQuestions.Add(q);
        }
    }
}
