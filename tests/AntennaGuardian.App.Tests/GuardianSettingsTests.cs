namespace AntennaGuardian.App.Tests;

using AntennaGuardian.Flex;

public sealed class GuardianSettingsTests
{
    [Fact]
    public void GetAntennaDisplayNameReturnsConfiguredName()
    {
        var settings = new GuardianSettings();
        settings.AntennaNames["ANT1"] = "  EFHW  ";

        Assert.Equal("EFHW", settings.GetAntennaDisplayName("ANT1"));
    }

    [Fact]
    public void GetAntennaDisplayNameFallsBackToFlexIdentifier()
    {
        var settings = new GuardianSettings();
        settings.AntennaNames["ANT2"] = "  ";

        Assert.Equal("ANT2", settings.GetAntennaDisplayName("ANT2"));
        Assert.Equal("RX_A", settings.GetAntennaDisplayName("RX_A"));
    }

    [Fact]
    public void CloneKeepsAntennaNamesIndependent()
    {
        var settings = new GuardianSettings();
        settings.AntennaNames["ANT1"] = "Hexbeam";

        var clone = settings.Clone();
        clone.AntennaNames["ANT1"] = "EFHW";

        Assert.Equal("Hexbeam", settings.AntennaNames["ANT1"]);
        Assert.Equal("EFHW", clone.AntennaNames["ANT1"]);
    }

    [Fact]
    public void CloneKeepsAutomaticUpdatePreference()
    {
        var settings = new GuardianSettings { AutomaticallyCheckForUpdates = false };

        var clone = settings.Clone();

        Assert.False(clone.AutomaticallyCheckForUpdates);
    }

    [Fact]
    public void LegacySettingsDefaultToDirectConnection()
    {
        var settings = new GuardianSettings { RadioHost = "192.0.2.10" };

        var options = settings.BuildRadioConnectionOptions();

        Assert.Equal(RadioConnectionMode.Direct, options.Mode);
        Assert.Equal("192.0.2.10", options.DirectHost);
    }

    [Fact]
    public void DiscoverySettingsBuildSerialAndIpSelector()
    {
        var settings = new GuardianSettings
        {
            RadioConnectionMode = RadioConnectionMode.Discovery,
            RadioSerial = " 1234-ABCD ",
            RadioDiscoveryIp = " 192.0.2.20 ",
        };

        var options = settings.BuildRadioConnectionOptions();

        Assert.Equal(RadioConnectionMode.Discovery, options.Mode);
        Assert.Equal("1234-ABCD", options.Serial);
        Assert.Equal("192.0.2.20", options.DiscoveryIp);
    }

    [Fact]
    public void CloneKeepsRadioDiscoverySettings()
    {
        var settings = new GuardianSettings
        {
            RadioConnectionMode = RadioConnectionMode.Discovery,
            RadioSerial = "1234-ABCD",
            RadioDiscoveryIp = "192.0.2.20",
        };

        var clone = settings.Clone();

        Assert.Equal(RadioConnectionMode.Discovery, clone.RadioConnectionMode);
        Assert.Equal("1234-ABCD", clone.RadioSerial);
        Assert.Equal("192.0.2.20", clone.RadioDiscoveryIp);
    }

    [Fact]
    public async Task SettingsStoreRoundTripsRadioDiscoverySelection()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"AntennaGuardianSettings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(settingsPath);
            await store.SaveAsync(new GuardianSettings
            {
                RadioConnectionMode = RadioConnectionMode.Discovery,
                RadioSerial = "1234-ABCD",
                RadioDiscoveryIp = "192.0.2.20",
            });

            var loaded = await store.LoadAsync();

            Assert.Equal(RadioConnectionMode.Discovery, loaded.RadioConnectionMode);
            Assert.Equal("1234-ABCD", loaded.RadioSerial);
            Assert.Equal("192.0.2.20", loaded.RadioDiscoveryIp);
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }
}
