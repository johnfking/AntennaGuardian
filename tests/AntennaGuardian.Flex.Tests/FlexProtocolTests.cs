using AntennaGuardian.Core;
using AntennaGuardian.Flex;

namespace AntennaGuardian.Flex.Tests;

public sealed class FlexProtocolTests
{
    [Fact]
    public void TransmitStatusProducesAuthoritativeTxContext()
    {
        var protocol = new FlexProtocol();

        var events = protocol.Feed(
            "S791DFC6F|transmit freq=14.302000 rfpower=50 tx_antenna=ANT1");

        var changed = Assert.IsType<TxContextChanged>(Assert.Single(events));
        Assert.Equal(14.302, changed.Context.FrequencyMhz);
        Assert.Equal("ANT1", changed.Context.TxAntenna);
    }

    [Fact]
    public void InvalidTransmitAntennaClearsStaleTxContext()
    {
        var protocol = new FlexProtocol();
        protocol.Feed(
            "S791DFC6F|transmit freq=14.074000 rfpower=50 tx_antenna=ANT1");

        var events = protocol.Feed(
            "S791DFC6F|transmit freq=14.074000 rfpower=50 tx_antenna=INVALID");

        var changed = Assert.IsType<TxContextChanged>(Assert.Single(events));
        Assert.Null(changed.Context.FrequencyMhz);
        Assert.Null(changed.Context.TxAntenna);
    }

    [Theory]
    [InlineData("PTT_REQUESTED", typeof(PttRequested))]
    [InlineData("UNKEY_REQUESTED", typeof(UnkeyRequested))]
    [InlineData("TRANSMITTING", typeof(RadioTransmitting))]
    public void InterlockStatusProducesGuardianEvent(string state, Type expectedType)
    {
        var protocol = new FlexProtocol();

        var events = protocol.Feed(
            $"S0|interlock tx_client_handle=0x1234 state={state} source=SW tx_allowed=1");

        Assert.IsType(expectedType, Assert.Single(events));
    }
}
