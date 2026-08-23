namespace AntennaGuardian.App.Tests;

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
}
