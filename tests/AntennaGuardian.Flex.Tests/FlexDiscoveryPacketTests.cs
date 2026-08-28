using System.Buffers.Binary;
using System.Text;

namespace AntennaGuardian.Flex.Tests;

public sealed class FlexDiscoveryPacketTests
{
    [Fact]
    public void ParsesFlexDiscoveryPacket()
    {
        var packet = BuildPacket(
            "serial=1234-5678-6600-ABCD ip=192.0.2.20 port=4992 model=FLEX-6600 nickname=Shack");

        var parsed = FlexDiscoveryPacket.TryParse(packet, out var radio);

        Assert.True(parsed);
        Assert.Equal("1234-5678-6600-ABCD", radio.Serial);
        Assert.Equal("192.0.2.20", radio.Host);
        Assert.Equal(4992, radio.Port);
        Assert.Equal("FLEX-6600", radio.Model);
        Assert.Equal("Shack", radio.Nickname);
    }

    [Fact]
    public void UsesNameWhenNicknameIsMissing()
    {
        var packet = BuildPacket("serial=ABC ip=192.0.2.21 port=4992 name=Workshop");

        Assert.True(FlexDiscoveryPacket.TryParse(packet, out var radio));
        Assert.Equal("Workshop", radio.Nickname);
    }

    [Theory]
    [InlineData("1234-ABCD", "", true)]
    [InlineData("1234-abcd", "192.0.2.20", true)]
    [InlineData("", "192.0.2.20", true)]
    [InlineData("OTHER", "", false)]
    [InlineData("1234-ABCD", "192.0.2.99", false)]
    public void MatchesConfiguredSelectors(string serial, string ip, bool expected)
    {
        var options = RadioConnectionOptions.Discover(serial, ip);
        var radio = new DiscoveredRadio("192.0.2.20", 4992, "1234-ABCD", "FLEX-6600", "Shack");

        Assert.Equal(expected, FlexDiscoveryPacket.Matches(options, radio));
    }

    [Fact]
    public void RejectsPacketsOutsideFlexDiscoveryClass()
    {
        var packet = BuildPacket("serial=ABC ip=192.0.2.20 port=4992");
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12), 0x12345678);

        Assert.False(FlexDiscoveryPacket.TryParse(packet, out _));
    }

    [Theory]
    [InlineData("serial=ABC ip=192.0.2.20")]
    [InlineData("serial=ABC ip=not-an-ip port=4992")]
    [InlineData("serial=ABC ip=192.0.2.20 port=0")]
    [InlineData("ip=192.0.2.20 port=4992")]
    public void RejectsIncompleteOrInvalidPayload(string payload)
    {
        Assert.False(FlexDiscoveryPacket.TryParse(BuildPacket(payload), out _));
    }

    private static byte[] BuildPacket(string payload)
    {
        var payloadBytes = Encoding.ASCII.GetBytes(payload);
        var packetLength = 16 + payloadBytes.Length;
        var paddedLength = (packetLength + 3) / 4 * 4;
        var packet = new byte[paddedLength];
        var header = 0x38000000u | (uint)(paddedLength / 4);
        BinaryPrimitives.WriteUInt32BigEndian(packet, header);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), 0x00000800);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12), 0x534cffff);
        payloadBytes.CopyTo(packet, 16);
        return packet;
    }
}
