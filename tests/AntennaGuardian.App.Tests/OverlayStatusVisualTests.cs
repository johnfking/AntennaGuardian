using AntennaGuardian.Core;

namespace AntennaGuardian.App.Tests;

public sealed class OverlayStatusVisualTests
{
    [Theory]
    [InlineData(null, "10.0.0.107", "10.0.0.107")]
    [InlineData("Shack FLEX-6600", "10.0.0.107", "Shack FLEX-6600 · 10.0.0.107")]
    [InlineData("  Shack  ", "flex.local", "Shack · flex.local")]
    public void RadioIdentityIncludesNicknameWhenAvailable(
        string? nickname,
        string host,
        string expected)
    {
        Assert.Equal(expected, MainWindow.FormatRadioIdentity(nickname, host));
    }

    [Fact]
    public void ArmedWithoutTxContextDisplaysProtected()
    {
        var decision = new PolicyEngine().Evaluate(
            new TxContext(null, null),
            AntennaPolicy.Empty);
        var status = new GuardianStatus(
            ProtectionState.Armed,
            new TxContext(null, null),
            decision,
            "9",
            "Interlock armed.");

        var visual = OverlayStatusVisuals.For(status);

        Assert.Equal("PROTECTED", visual.Label);
    }

    [Fact]
    public void BlockingStateStillDisplaysTxBlocked()
    {
        var decision = new PolicyEngine().Evaluate(
            new TxContext(14.074, "ANT2"),
            AntennaPolicy.FromAllowed(("ANT1", "20m")));
        var status = new GuardianStatus(
            ProtectionState.Blocking,
            new TxContext(14.074, "ANT2"),
            decision,
            "9",
            decision.Message);

        var visual = OverlayStatusVisuals.For(status);

        Assert.Equal("TX BLOCKED", visual.Label);
    }
}
