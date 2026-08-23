using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using AntennaGuardian.Core;

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

    public SettingsWindow(
        GuardianSettings settings,
        ObservableCollection<string> activity)
    {
        _settings = settings;
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
        Ant1NameTextBox.Text = settings.GetAntennaDisplayName("ANT1");
        Ant2NameTextBox.Text = settings.GetAntennaDisplayName("ANT2");
        ProtectionCheckBox.IsChecked = settings.ProtectionEnabled;
        AlwaysOnTopCheckBox.IsChecked = settings.AlwaysOnTop;
        ClickThroughCheckBox.IsChecked = settings.ClickThrough;
        OpacitySlider.Value = settings.OverlayOpacity;
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
        if (host.Length == 0 || (!IPAddress.TryParse(host, out _) && !Uri.CheckHostName(host).Equals(UriHostNameType.Dns)))
        {
            System.Windows.MessageBox.Show(
                this,
                "Enter a valid radio IP address or hostname.",
                "AntennaGuardian",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            RadioHostTextBox.Focus();
            return;
        }

        _settings.RadioHost = host;
        _settings.ProtectionEnabled = ProtectionCheckBox.IsChecked == true;
        _settings.AlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true;
        _settings.ClickThrough = ClickThroughCheckBox.IsChecked == true;
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

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static string NormalizeAntennaName(string name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
}
