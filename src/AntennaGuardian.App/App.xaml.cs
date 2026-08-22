using System.Windows;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using AntennaGuardian.Core;

namespace AntennaGuardian.App;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var previewProtected = e.Args.Contains(
            "--preview-protected",
            StringComparer.OrdinalIgnoreCase);
        var preview = previewProtected
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
            };
        }

        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            var settingsWindow = new SettingsWindow(settings, new ObservableCollection<string>());
            MainWindow = settingsWindow;
            settingsWindow.ShowDialog();
            Shutdown();
            return;
        }

        var window = new MainWindow(settings, store);
        MainWindow = window;
        window.Show();
        if (previewProtected)
        {
            var context = new TxContext(14.074, "ANT1");
            var decision = new PolicyEngine().Evaluate(
                context,
                AntennaPolicy.FromAllowed(("ANT1", "20m")));
            var status = new GuardianStatus(
                ProtectionState.Armed,
                context,
                decision,
                null,
                "Interlock armed.");
            _ = window.Dispatcher.BeginInvoke(
                () => window.ShowPreviewStatus(status),
                DispatcherPriority.ApplicationIdle);
        }
    }
}
