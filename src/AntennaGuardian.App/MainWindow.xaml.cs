using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AntennaGuardian.Core;
using AntennaGuardian.Flex;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace AntennaGuardian.App;

public partial class MainWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private readonly SettingsStore _settingsStore;
    private readonly ObservableCollection<string> _activity = [];
    private readonly Drawing.Icon _applicationIcon;
    private readonly Forms.NotifyIcon _trayIcon;
    private GuardianSettings _settings;
    private GuardianRuntime? _runtime;
    private bool _exiting;
    private string? _radioNickname;
    private string? _radioDisplayHostOverride;
    private string _stateLabel = "OFFLINE";
    private System.Windows.Media.Brush _stateBrush = System.Windows.Media.Brushes.White;

    public MainWindow(GuardianSettings settings, SettingsStore settingsStore)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        InitializeComponent();
        VersionText.Text = $"v{GetType().Assembly.GetName().Version?.ToString(3)}";
        UpdateRadioIdentity(null);
        _applicationIcon = LoadApplicationIcon();

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _applicationIcon,
            Text = "AntennaGuardian - Offline",
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowOverlay);
        RebuildTrayMenu();

        Loaded += MainWindow_Loaded;
        LocationChanged += (_, _) => CapturePosition();
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Topmost = _settings.AlwaysOnTop;
        Opacity = Math.Clamp(_settings.OverlayOpacity, 0.55, 1.0);
        if (_settings.OverlayLeft is not null && _settings.OverlayTop is not null)
        {
            Left = _settings.OverlayLeft.Value;
            Top = _settings.OverlayTop.Value;
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - Width - 24;
            Top = SystemParameters.WorkArea.Top + 24;
        }

        ApplyClickThrough(_settings.ClickThrough);
        SetOfflineVisual(_settings.ProtectionEnabled
            ? "Waiting to start"
            : "Protection disabled");

        if (_settings.ProtectionEnabled)
        {
            await StartProtectionAsync();
        }
    }

    private Task StartProtectionAsync()
    {
        if (_runtime is not null)
        {
            return Task.CompletedTask;
        }

        _runtime = new GuardianRuntime(
            _settings.RadioHost,
            _settings.BuildPolicy(),
            _settings.InterlockAntennas);
        _runtime.StatusChanged += Runtime_StatusChanged;
        _runtime.Activity += Runtime_Activity;
        _runtime.RadioIdentityChanged += Runtime_RadioIdentityChanged;
        _runtime.Start();
        RebuildTrayMenu();
        return Task.CompletedTask;
    }

    private async Task StopProtectionAsync()
    {
        var runtime = _runtime;
        _runtime = null;
        if (runtime is not null)
        {
            runtime.StatusChanged -= Runtime_StatusChanged;
            runtime.Activity -= Runtime_Activity;
            runtime.RadioIdentityChanged -= Runtime_RadioIdentityChanged;
            await runtime.DisposeAsync();
        }
        UpdateRadioIdentity(null);
        SetOfflineVisual("Protection disabled");
        RebuildTrayMenu();
    }

    private void Runtime_StatusChanged(GuardianStatus status)
    {
        Dispatcher.Invoke(() => ApplyStatus(status));
    }

    private void Runtime_Activity(string message)
    {
        Dispatcher.Invoke(() =>
        {
            _activity.Add($"{DateTime.Now:HH:mm:ss}  {message}");
            while (_activity.Count > 400)
            {
                _activity.RemoveAt(0);
            }
        });
    }

    private void Runtime_RadioIdentityChanged(string nickname)
    {
        Dispatcher.Invoke(() => UpdateRadioIdentity(nickname));
    }

    private void UpdateRadioIdentity(string? nickname)
    {
        _radioNickname = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim();
        RenderStateText();
    }

    internal static string FormatRadioIdentity(string? nickname, string host) =>
        string.IsNullOrWhiteSpace(nickname)
            ? host
            : $"{nickname.Trim()} · {host}";

    private void ApplyStatus(GuardianStatus status)
    {
        var visual = OverlayStatusVisuals.For(status);
        var label = visual.Label;
        var color = MediaColor.FromRgb(visual.Red, visual.Green, visual.Blue);
        var brush = new SolidColorBrush(color);
        var haloBrush = new SolidColorBrush(MediaColor.FromArgb(54, color.R, color.G, color.B));
        _stateLabel = label;
        _stateBrush = brush;
        RenderStateText();
        StateRail.Background = brush;
        ShieldHalo.Fill = haloBrush;
        ShieldHalo.Stroke = brush;
        ShieldIcon.Foreground = brush;
        Frame.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(52, 57, 68));
        DetailText.Text = FormatDetail(status);
        _trayIcon.Text = $"AntennaGuardian - {label}";
    }

    internal void ShowPreviewStatus(GuardianStatus status) => ApplyStatus(status);

    private string FormatDetail(GuardianStatus status)
    {
        var parts = new List<string>();
        if (status.Decision.Band != "Unknown")
        {
            parts.Add(status.Decision.Band);
        }
        if (!string.IsNullOrWhiteSpace(status.Context.TxAntenna))
        {
            parts.Add(_settings.GetAntennaDisplayName(status.Context.TxAntenna));
        }
        if (status.Context.FrequencyMhz is not null)
        {
            parts.Add($"{status.Context.FrequencyMhz:0.000000} MHz");
        }
        return parts.Count == 0 ? status.Message : string.Join("  ·  ", parts);
    }

    private void SetOfflineVisual(string detail)
    {
        var neutral = new SolidColorBrush(MediaColor.FromRgb(111, 120, 128));
        _stateLabel = "OFFLINE";
        _stateBrush = new SolidColorBrush(MediaColor.FromRgb(214, 219, 224));
        RenderStateText();
        DetailText.Text = detail;
        StateRail.Background = neutral;
        ShieldHalo.Fill = new SolidColorBrush(MediaColor.FromRgb(27, 32, 40));
        ShieldHalo.Stroke = neutral;
        ShieldIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(154, 163, 174));
        Frame.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(52, 57, 68));
        _trayIcon.Text = "AntennaGuardian - Offline";
    }

    private void RenderStateText()
    {
        var identity = FormatRadioIdentity(
            _radioNickname,
            _radioDisplayHostOverride ?? _settings.RadioHost);
        StateText.Inlines.Clear();
        StateText.Inlines.Add(new Run(_stateLabel)
        {
            Foreground = _stateBrush,
        });
        StateText.Inlines.Add(new Run($"  {identity}")
        {
            BaselineAlignment = BaselineAlignment.Baseline,
            FontSize = 9,
            FontWeight = FontWeights.Normal,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(143, 152, 163)),
        });
        StateText.ToolTip = $"{_stateLabel} · {identity}";
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowSettingsAsync();
    }

    private async Task ShowSettingsAsync()
    {
        ApplyClickThrough(false);
        var dialog = new SettingsWindow(_settings.Clone(), _activity)
        {
            Owner = this,
        };
        var accepted = dialog.ShowDialog() == true && dialog.Result is not null;
        if (!accepted)
        {
            _settings.SettingsWindowWidth = dialog.RememberedWidth;
            _settings.SettingsWindowHeight = dialog.RememberedHeight;
            await _settingsStore.SaveAsync(_settings);
            ApplyClickThrough(_settings.ClickThrough);
            return;
        }

        dialog.Result!.SettingsWindowWidth = dialog.RememberedWidth;
        dialog.Result.SettingsWindowHeight = dialog.RememberedHeight;
        var previousEnabled = _settings.ProtectionEnabled;
        var connectionChanged = !string.Equals(
            _settings.RadioHost,
            dialog.Result.RadioHost,
            StringComparison.OrdinalIgnoreCase);
        _settings = dialog.Result;
        UpdateRadioIdentity(null);
        Topmost = _settings.AlwaysOnTop;
        Opacity = Math.Clamp(_settings.OverlayOpacity, 0.55, 1.0);
        await _settingsStore.SaveAsync(_settings);

        if (previousEnabled && (!_settings.ProtectionEnabled || connectionChanged))
        {
            await StopProtectionAsync();
        }
        if (_settings.ProtectionEnabled && _runtime is null)
        {
            await StartProtectionAsync();
        }
        else if (_runtime is not null)
        {
            await _runtime.UpdatePolicyAsync(_settings.BuildPolicy());
        }

        ApplyClickThrough(_settings.ClickThrough);
        RebuildTrayMenu();
    }

    public Task OpenSettingsAsync() => ShowSettingsAsync();

    internal void ShowPreviewRadioIdentity(string nickname, string host)
    {
        _radioDisplayHostOverride = host;
        UpdateRadioIdentity(nickname);
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !IsInsideButton(e.OriginalSource as DependencyObject))
        {
            DragMove();
        }
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Button)
            {
                return true;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void ShowOverlay()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void RebuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show overlay", null, (_, _) => Dispatcher.Invoke(ShowOverlay));
        var protection = new Forms.ToolStripMenuItem("Protection enabled")
        {
            Checked = _settings.ProtectionEnabled,
            CheckOnClick = true,
        };
        protection.Click += async (_, _) =>
        {
            _settings.ProtectionEnabled = protection.Checked;
            await _settingsStore.SaveAsync(_settings);
            if (_settings.ProtectionEnabled)
            {
                await StartProtectionAsync();
            }
            else
            {
                await StopProtectionAsync();
            }
        };
        menu.Items.Add(protection);

        var clickThrough = new Forms.ToolStripMenuItem("Click-through overlay")
        {
            Checked = _settings.ClickThrough,
            CheckOnClick = true,
        };
        clickThrough.Click += async (_, _) =>
        {
            _settings.ClickThrough = clickThrough.Checked;
            Dispatcher.Invoke(() => ApplyClickThrough(_settings.ClickThrough));
            await _settingsStore.SaveAsync(_settings);
        };
        menu.Items.Add(clickThrough);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.BeginInvoke(ShowSettingsAsync));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.BeginInvoke(ExitAsync));
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.ContextMenuStrip = menu;
    }

    private async Task ExitAsync()
    {
        _exiting = true;
        CapturePosition();
        await _settingsStore.SaveAsync(_settings);
        await StopProtectionAsync();
        _trayIcon.Visible = false;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void CapturePosition()
    {
        if (WindowState == WindowState.Normal)
        {
            _settings.OverlayLeft = Left;
            _settings.OverlayTop = Top;
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _trayIcon.Dispose();
        _applicationIcon.Dispose();
    }

    private static Drawing.Icon LoadApplicationIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("Assets/AntennaGuardian.ico", UriKind.Relative));
        if (resource is null)
        {
            return (Drawing.Icon)Drawing.SystemIcons.Shield.Clone();
        }

        using (resource.Stream)
        using (var icon = new Drawing.Icon(resource.Stream))
        {
            return (Drawing.Icon)icon.Clone();
        }
    }

    private void ApplyClickThrough(bool enabled)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(
            handle,
            GwlExStyle,
            enabled ? style | WsExTransparent : style & ~WsExTransparent);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr window, int index, int newStyle);
}
