using System.Configuration;
using System.Data;
using System.Windows;
using AutoPilotAgent.Automation.Services;
using AutoPilotAgent.Automation.UIA;
using AutoPilotAgent.Automation.Win32;
using AutoPilotAgent.Core.Interfaces;
using AutoPilotAgent.Core.Services;
using AutoPilotAgent.Logging;
using AutoPilotAgent.OpenAI;
using AutoPilotAgent.Policy;
using AutoPilotAgent.Storage.Paths;
using AutoPilotAgent.Storage.Secrets;
using AutoPilotAgent.Storage.Sqlite;
using AutoPilotAgent.UI.Services;
using AutoPilotAgent.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AutoPilotAgent.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private GlobalKillSwitch? _killSwitch;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sink = new InMemoryLogSink();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog((_, _, cfg) =>
            {
                cfg.MinimumLevel.Information();
                cfg.WriteTo.Sink(sink);
                cfg.WriteTo.File(AppPaths.GetLogFilePath(), rollingInterval: RollingInterval.Day, shared: true);
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton<UserPreferences>();
                services.AddSingleton(new ApiKeyStore(AppPaths.GetSecretsPath()));
                services.AddSingleton(new SqliteRepository(AppPaths.GetDatabasePath()));

                services.AddSingleton(new OpenAIOptions());
                services.AddHttpClient<OpenAIClient>();
                services.AddSingleton<Func<string?>>(sp => () => sp.GetRequiredService<ApiKeyStore>().LoadApiKey());
                services.AddSingleton<IPlanService, OpenAIPlanService>();
                services.AddSingleton<IActionService, OpenAIActionService>();

                services.AddSingleton<InputSender>();
                services.AddSingleton<WindowManager>();
                services.AddSingleton<UIAutomationHelper>();
                services.AddSingleton<ObservationService>();
                services.AddSingleton<IObservationService>(sp => sp.GetRequiredService<ObservationService>());
                services.AddSingleton<ActionExecutor>();

                services.AddSingleton<IPolicyEngine, DefaultPolicyEngine>();
                services.AddSingleton<PolicyEnforcedActionExecutor>(sp =>
                    new PolicyEnforcedActionExecutor(
                        sp.GetRequiredService<ActionExecutor>(),
                        sp.GetRequiredService<IPolicyEngine>(),
                        sp.GetRequiredService<IObservationService>(),
                        () => sp.GetRequiredService<UserPreferences>().AutoApprove));
                services.AddSingleton<IActionExecutor>(sp => sp.GetRequiredService<PolicyEnforcedActionExecutor>());

                services.AddSingleton<IArmState, ArmState>();
                services.AddSingleton<IPlanContext, PlanContext>();
                services.AddSingleton<IStepValidationService, StepValidationService>();
                services.AddSingleton<IAgentOrchestrator, AgentOrchestrator>();

                services.AddSingleton<IUserConfirmationService, WpfUserConfirmationService>();

                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        var repo = _host.Services.GetRequiredService<SqliteRepository>();
        sink.LogEmitted += entry =>
        {
            try
            {
                repo.InsertLog(entry.Level, entry.Message);
            }
            catch
            {
            }
        };

        _killSwitch = new GlobalKillSwitch(() => _host.Services.GetRequiredService<IAgentOrchestrator>().Stop());
        _killSwitch.Start();

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _killSwitch?.Dispose();

        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(2));
            _host.Dispose();
        }

        Log.CloseAndFlush();

        base.OnExit(e);
    }
}

