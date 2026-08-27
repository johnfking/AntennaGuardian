using System.Windows;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using AntennaGuardian.Core;
using Velopack;

namespace AntennaGuardian.App;

public partial class App : System.Windows.Application
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .SetArgs(args)
            .SetAutoApplyOnStartup(false)
            .Run();

        var application = new App();
        application.InitializeComponent();
        application.Run();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var previewProtected = e.Args.Contains(
            "--preview-protected",
            StringComparer.OrdinalIgnoreCase);
        var previewBlocked = e.Args.Contains(
            "--preview-blocked",
            StringComparer.OrdinalIgnoreCase);
        var previewTransmitting = e.Args.Contains(
            "--preview-transmitting",
            StringComparer.OrdinalIgnoreCase);
        var preview = previewProtected
            || previewBlocked
            || previewTransmitting
            || e.Args.Contains("--preview", StringComparer.OrdinalIgnoreCase);
        var store = preview
            ? new SettingsStore(Path.Combine(
                Path.GetTempPath(),
                "AntennaGuardian",
                "preview-settings.json"))
            : new SettingsStore();
        GuardianSettings settings;
        try
        {
            settings = await store.LoadAsync();
        }
        catch
        {
            settings = new GuardianSettings();
        }

        if (preview)
        {
            settings = new GuardianSettings
            {
                RadioHost = "127.0.0.1",
                ProtectionEnabled = false,
                AntennaNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ANT1"] = "EFHW",
                    ["ANT2"] = "Hexbeam",
                },
                AllowedBandsByAntenna = new Dictionary<string, List<string>>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["ANT1"] = ["160m", "80m", "60m", "40m", "30m", "20m", "17m"],
                    ["ANT2"] = ["20m", "17m", "15m", "12m", "10m", "6m"],
                },
            };
        }

        var updateService = new AppUpdateService(new VelopackUpdateBackend());
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            var settingsWindow = new SettingsWindow(
                settings,
                new ObservableCollection<string>(),
                updateService,
                () => Task.CompletedTask);
            MainWindow = settingsWindow;
            settingsWindow.ShowDialog();
            Shutdown();
            return;
        }

        var window = new MainWindow(settings, store, updateService);
        MainWindow = window;
        window.Show();
        if (preview)
        {
            window.ShowPreviewRadioIdentity("Shack FLEX-6600", "10.0.0.107");
        }
        if (previewProtected || previewBlocked || previewTransmitting)
        {
            var context = new TxContext(
                14.074,
                previewBlocked ? "ANT2" : "ANT1");
            var decision = new PolicyEngine().Evaluate(
                context,
                AntennaPolicy.FromAllowed(("ANT1", "20m")));
            var status = new GuardianStatus(
                previewBlocked
                    ? ProtectionState.Blocking
                    : previewTransmitting
                        ? ProtectionState.Transmitting
                        : ProtectionState.Armed,
                context,
                decision,
                null,
                previewBlocked
                    ? "ANT2 is blocked on 20m."
                    : previewTransmitting
                        ? "Transmission allowed."
                        : "Interlock armed.");
            _ = window.Dispatcher.BeginInvoke(
                () => window.ShowPreviewStatus(status),
                DispatcherPriority.ApplicationIdle);
        }
    }
}
