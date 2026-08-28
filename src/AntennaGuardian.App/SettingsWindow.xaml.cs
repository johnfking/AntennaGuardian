using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Windows;
using AntennaGuardian.Core;
using AntennaGuardian.Flex;

namespace AntennaGuardian.App;

public sealed class BandRow
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public bool Ant1Allowed { get; set; }
    public bool Ant2Allowed { get; set; }
}

public partial class SettingsWindow : Window
{
    private readonly GuardianSettings _settings;
    private readonly IAppUpdateService _updateService;
    private readonly Func<Task> _installUpdate;

    public SettingsWindow(
        GuardianSettings settings,
        ObservableCollection<string> activity,
        IAppUpdateService updateService,
        Func<Task> installUpdate)
    {
        _settings = settings;
        _updateService = updateService;
        _installUpdate = installUpdate;
        Bands = new ObservableCollection<BandRow>(BandCatalog.NativeBands.Select(band =>
            new BandRow
            {
                Name = band.Name,
                Label = band.Name.TrimEnd('m'),
                Ant1Allowed = IsAllowed("ANT1", band.Name),
                Ant2Allowed = IsAllowed("ANT2", band.Name),
            }));
        Activity = activity;
        InitializeComponent();
        DataContext = this;

        var maximumWidth = Math.Max(MinWidth, SystemParameters.WorkArea.Width - 32);
        var maximumHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 32);
        Width = Math.Clamp(settings.SettingsWindowWidth, MinWidth, maximumWidth);
        Height = Math.Clamp(settings.SettingsWindowHeight, MinHeight, maximumHeight);

        RadioHostTextBox.Text = settings.RadioHost;
        RadioSerialTextBox.Text = settings.RadioSerial;
        RadioDiscoveryIpTextBox.Text = settings.RadioDiscoveryIp;
        ApplyRadioMode(settings.RadioConnectionMode);
        Ant1NameTextBox.Text = settings.GetAntennaDisplayName("ANT1");
        Ant2NameTextBox.Text = settings.GetAntennaDisplayName("ANT2");
        ProtectionCheckBox.IsChecked = settings.ProtectionEnabled;
        AlwaysOnTopCheckBox.IsChecked = settings.AlwaysOnTop;
        ClickThroughCheckBox.IsChecked = settings.ClickThrough;
        AutomaticUpdatesCheckBox.IsChecked = settings.AutomaticallyCheckForUpdates;
        OpacitySlider.Value = settings.OverlayOpacity;
        ApplyUpdateState(_updateService.State);
        _updateService.StateChanged += UpdateService_StateChanged;
        Closed += (_, _) => _updateService.StateChanged -= UpdateService_StateChanged;
    }

    public ObservableCollection<BandRow> Bands { get; }
    public ObservableCollection<string> Activity { get; }
    public GuardianSettings? Result { get; private set; }
    public double RememberedWidth => WindowState == WindowState.Normal
        ? ActualWidth
        : RestoreBounds.Width;
    public double RememberedHeight => WindowState == WindowState.Normal
        ? ActualHeight
        : RestoreBounds.Height;

    private bool IsAllowed(string antenna, string band) =>
        _settings.AllowedBandsByAntenna.TryGetValue(antenna, out var bands)
        && bands.Contains(band, StringComparer.OrdinalIgnoreCase);

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var host = RadioHostTextBox.Text.Trim();
        var serial = RadioSerialTextBox.Text.Trim();
        var discoveryIp = RadioDiscoveryIpTextBox.Text.Trim();
        if (DiscoveryModeButton.IsChecked == true)
        {
            if (serial.Length == 0 && discoveryIp.Length == 0)
            {
                ShowRadioValidation("Enter a radio serial number, an IP pin, or both.", RadioSerialTextBox);
                return;
            }
            if (serial.Length > 64 || serial.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            {
                ShowRadioValidation(
                    "The serial number may contain letters, digits, hyphens, underscores, and periods.",
                    RadioSerialTextBox);
                return;
            }
            if (discoveryIp.Length > 0
                && (!IPAddress.TryParse(discoveryIp, out var address)
                    || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork))
            {
                ShowRadioValidation("Enter a valid IPv4 address for the optional IP pin.", RadioDiscoveryIpTextBox);
                return;
            }
        }
        else if (host.Length == 0
            || (!IPAddress.TryParse(host, out _)
                && !Uri.CheckHostName(host).Equals(UriHostNameType.Dns)))
        {
            ShowRadioValidation("Enter a valid radio IP address or hostname.", RadioHostTextBox);
            return;
        }

        _settings.RadioHost = host;
        _settings.RadioConnectionMode = DiscoveryModeButton.IsChecked == true
            ? RadioConnectionMode.Discovery
            : RadioConnectionMode.Direct;
        _settings.RadioSerial = serial;
        _settings.RadioDiscoveryIp = discoveryIp;
        _settings.ProtectionEnabled = ProtectionCheckBox.IsChecked == true;
        _settings.AlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true;
        _settings.ClickThrough = ClickThroughCheckBox.IsChecked == true;
        _settings.AutomaticallyCheckForUpdates = AutomaticUpdatesCheckBox.IsChecked == true;
        _settings.OverlayOpacity = OpacitySlider.Value;
        _settings.AllowedBandsByAntenna = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["ANT1"] = Bands.Where(row => row.Ant1Allowed).Select(row => row.Name).ToList(),
            ["ANT2"] = Bands.Where(row => row.Ant2Allowed).Select(row => row.Name).ToList(),
        };
        _settings.AntennaNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANT1"] = NormalizeAntennaName(Ant1NameTextBox.Text, "ANT1"),
            ["ANT2"] = NormalizeAntennaName(Ant2NameTextBox.Text, "ANT2"),
        };
        Result = _settings;
        DialogResult = true;
    }

    private void DiscoveryModeButton_Checked(object sender, RoutedEventArgs e) =>
        ApplyRadioMode(RadioConnectionMode.Discovery);

    private void DirectModeButton_Checked(object sender, RoutedEventArgs e) =>
        ApplyRadioMode(RadioConnectionMode.Direct);

    private void ApplyRadioMode(RadioConnectionMode mode)
    {
        var discovery = mode == RadioConnectionMode.Discovery;
        DiscoveryModeButton.IsChecked = discovery;
        DirectModeButton.IsChecked = !discovery;
        DiscoveryFields.Visibility = discovery ? Visibility.Visible : Visibility.Collapsed;
        DirectFields.Visibility = discovery ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowRadioValidation(string message, System.Windows.Controls.Control control)
    {
        System.Windows.MessageBox.Show(
            this,
            message,
            "AntennaGuardian",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        control.Focus();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e) =>
        await _updateService.CheckForUpdatesAsync();

    private async void DownloadUpdateButton_Click(object sender, RoutedEventArgs e) =>
        await _updateService.DownloadUpdateAsync();

    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e) =>
        await _installUpdate();

    private void OpenReleasesButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/johnfking/AntennaGuardian/releases/latest",
            UseShellExecute = true,
        });
    }

    private void UpdateService_StateChanged(AppUpdateState state)
    {
        Dispatcher.BeginInvoke(() => ApplyUpdateState(state));
    }

    private void ApplyUpdateState(AppUpdateState state)
    {
        CurrentVersionText.Text = $"v{state.CurrentVersion}";
        UpdateStatusText.Text = state.Message;
        UpdateProgress.Value = state.ProgressPercent;
        UpdateProgress.Visibility = state.Phase == AppUpdatePhase.Downloading
            ? Visibility.Visible
            : Visibility.Collapsed;
        var externallyManaged = state.Phase is AppUpdatePhase.Portable or AppUpdatePhase.Store;
        AutomaticUpdatesCheckBox.Visibility = externallyManaged
            ? Visibility.Collapsed
            : Visibility.Visible;
        CheckUpdateButton.IsEnabled = state.CanCheck;
        CheckUpdateButton.Visibility = state.Phase == AppUpdatePhase.Portable
            ? Visibility.Collapsed
            : Visibility.Visible;
        OpenReleasesButton.Visibility = state.Phase == AppUpdatePhase.Portable
            ? Visibility.Visible
            : Visibility.Collapsed;
        DownloadUpdateButton.IsEnabled = state.CanDownload;
        DownloadUpdateButton.Visibility = state.CanDownload
            ? Visibility.Visible
            : Visibility.Collapsed;
        InstallUpdateButton.IsEnabled = state.CanInstall;
        InstallUpdateButton.Visibility = state.CanInstall
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static string NormalizeAntennaName(string name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
}
