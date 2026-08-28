using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using AntennaGuardian.Core;
using AntennaGuardian.Flex;

namespace AntennaGuardian.App;

public sealed class GuardianSettings
{
    public string RadioHost { get; set; } = "127.0.0.1";
    public RadioConnectionMode RadioConnectionMode { get; set; } = RadioConnectionMode.Direct;
    public string RadioSerial { get; set; } = string.Empty;
    public string RadioDiscoveryIp { get; set; } = string.Empty;
    public bool ProtectionEnabled { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public bool ClickThrough { get; set; }
    public bool AutomaticallyCheckForUpdates { get; set; } = true;
    public double OverlayOpacity { get; set; } = 0.96;
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    public double SettingsWindowWidth { get; set; } = 760;
    public double SettingsWindowHeight { get; set; } = 640;
    public Dictionary<string, List<string>> AllowedBandsByAntenna { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ANT1"] = [],
            ["ANT2"] = [],
        };
    public Dictionary<string, string> AntennaNames { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ANT1"] = "ANT1",
            ["ANT2"] = "ANT2",
        };

    [JsonIgnore]
    public IReadOnlyList<string> InterlockAntennas => ["ANT1", "ANT2"];

    public AntennaPolicy BuildPolicy()
    {
        var cells = AllowedBandsByAntenna.SelectMany(pair =>
            pair.Value.Select(band => (pair.Key, band)));
        return AntennaPolicy.FromAllowed(cells.ToArray());
    }

    public RadioConnectionOptions BuildRadioConnectionOptions() =>
        RadioConnectionMode == RadioConnectionMode.Discovery
            ? RadioConnectionOptions.Discover(RadioSerial.Trim(), RadioDiscoveryIp.Trim())
            : RadioConnectionOptions.Direct(RadioHost.Trim());

    public string GetAntennaDisplayName(string antenna)
    {
        if (AntennaNames.TryGetValue(antenna, out var name)
            && !string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        return antenna;
    }

    public GuardianSettings Clone() => new()
    {
        RadioHost = RadioHost,
        RadioConnectionMode = RadioConnectionMode,
        RadioSerial = RadioSerial,
        RadioDiscoveryIp = RadioDiscoveryIp,
        ProtectionEnabled = ProtectionEnabled,
        AlwaysOnTop = AlwaysOnTop,
        ClickThrough = ClickThrough,
        AutomaticallyCheckForUpdates = AutomaticallyCheckForUpdates,
        OverlayOpacity = OverlayOpacity,
        OverlayLeft = OverlayLeft,
        OverlayTop = OverlayTop,
        SettingsWindowWidth = SettingsWindowWidth,
        SettingsWindowHeight = SettingsWindowHeight,
        AllowedBandsByAntenna = AllowedBandsByAntenna.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToList(),
            StringComparer.OrdinalIgnoreCase),
        AntennaNames = AntennaNames.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase),
    };
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public SettingsStore(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AntennaGuardian",
            "settings.json");
    }

    public string SettingsPath { get; }

    public async Task<GuardianSettings> LoadAsync()
    {
        if (!File.Exists(SettingsPath))
        {
            return new GuardianSettings();
        }

        await using var stream = File.OpenRead(SettingsPath);
        return await JsonSerializer.DeserializeAsync<GuardianSettings>(stream, JsonOptions)
            ?? new GuardianSettings();
    }

    public async Task SaveAsync(GuardianSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
        }

        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}
