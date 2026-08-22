using AntennaGuardian.Core;

namespace AntennaGuardian.Core.Tests;

public sealed class PolicyEngineTests
{
    [Fact]
    public void UnknownFrequencyIsBlocked()
    {
        var engine = new PolicyEngine();

        var decision = engine.Evaluate(
            new TxContext(FrequencyMhz: null, TxAntenna: "ANT1"),
            AntennaPolicy.Empty);

        Assert.False(decision.IsAllowed);
        Assert.Equal(DecisionReason.UnknownFrequency, decision.Reason);
        Assert.Equal("Unknown", decision.Band);
    }

    [Fact]
    public void ExplicitlyAllowedAntennaAndBandCanTransmit()
    {
        var engine = new PolicyEngine();
        var policy = AntennaPolicy.FromAllowed(("ANT1", "20m"));

        var decision = engine.Evaluate(
            new TxContext(FrequencyMhz: 14.074, TxAntenna: "ANT1"),
            policy);

        Assert.True(decision.IsAllowed);
        Assert.Equal(DecisionReason.Allowed, decision.Reason);
        Assert.Equal("20m", decision.Band);
    }

    [Fact]
    public void UncheckedMatrixCellIsBlocked()
    {
        var engine = new PolicyEngine();
        var policy = AntennaPolicy.FromAllowed(("ANT1", "20m"));

        var decision = engine.Evaluate(
            new TxContext(FrequencyMhz: 14.074, TxAntenna: "ANT2"),
            policy);

        Assert.False(decision.IsAllowed);
        Assert.Equal(DecisionReason.CombinationNotAllowed, decision.Reason);
        Assert.Equal("20m", decision.Band);
    }

    [Theory]
    [InlineData(1.9, "160m")]
    [InlineData(3.9, "80m")]
    [InlineData(5.357, "60m")]
    [InlineData(7.074, "40m")]
    [InlineData(10.136, "30m")]
    [InlineData(14.074, "20m")]
    [InlineData(18.1, "17m")]
    [InlineData(21.074, "15m")]
    [InlineData(24.915, "12m")]
    [InlineData(28.4, "10m")]
    [InlineData(50.313, "6m")]
    public void KnownAmateurBandsAreMapped(double frequencyMhz, string expectedBand)
    {
        var engine = new PolicyEngine();
        var policy = AntennaPolicy.FromAllowed(("ANT1", expectedBand));

        var decision = engine.Evaluate(new TxContext(frequencyMhz, "ANT1"), policy);

        Assert.True(decision.IsAllowed);
        Assert.Equal(expectedBand, decision.Band);
    }

    [Fact]
    public void UnknownAntennaIsBlocked()
    {
        var decision = new PolicyEngine().Evaluate(
            new TxContext(14.074, TxAntenna: null),
            AntennaPolicy.Empty);

        Assert.False(decision.IsAllowed);
        Assert.Equal(DecisionReason.UnknownAntenna, decision.Reason);
    }

    [Fact]
    public void FrequencyOutsideNativeCoverageIsBlocked()
    {
        var decision = new PolicyEngine().Evaluate(
            new TxContext(144.2, "ANT1"),
            AntennaPolicy.FromAllowed(("ANT1", "2m")));

        Assert.False(decision.IsAllowed);
        Assert.Equal(DecisionReason.FrequencyOutsideKnownBands, decision.Reason);
        Assert.Equal("Unknown", decision.Band);
    }
}
