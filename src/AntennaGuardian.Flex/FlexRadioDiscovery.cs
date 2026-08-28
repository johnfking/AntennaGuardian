using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AntennaGuardian.Flex;

public enum RadioConnectionMode
{
    Direct,
    Discovery,
}

public sealed record RadioConnectionOptions(
    RadioConnectionMode Mode,
    string DirectHost,
    string Serial,
    string DiscoveryIp)
{
    public static RadioConnectionOptions Direct(string host) =>
        new(RadioConnectionMode.Direct, host, string.Empty, string.Empty);

    public static RadioConnectionOptions Discover(string serial, string discoveryIp) =>
        new(RadioConnectionMode.Discovery, string.Empty, serial, discoveryIp);
}

public sealed record DiscoveredRadio(
    string Host,
    int Port,
    string Serial,
    string Model,
    string Nickname);

internal interface IFlexRadioDiscovery
{
    Task<DiscoveredRadio> WaitForMatchAsync(
        RadioConnectionOptions options,
        CancellationToken cancellationToken);
}

internal sealed class FlexRadioDiscovery : IFlexRadioDiscovery
{
    internal const int DiscoveryPort = 4992;
    private const int MaximumPacketSize = 4096;

    public async Task<DiscoveredRadio> WaitForMatchAsync(
        RadioConnectionOptions options,
        CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ExclusiveAddressUse = false,
        };
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

        var packet = new byte[MaximumPacketSize];
        EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            var result = await socket.ReceiveFromAsync(
                packet,
                SocketFlags.None,
                sender,
                cancellationToken);
            if (FlexDiscoveryPacket.TryParse(packet.AsSpan(0, result.ReceivedBytes), out var radio)
                && FlexDiscoveryPacket.Matches(options, radio))
            {
                return radio;
            }
        }
    }
}

internal static class FlexDiscoveryPacket
{
    private const uint ExtendedDataWithStream = 3;
    private const uint ClassPresent = 0x08000000;
    private const uint DiscoveryStream = 0x00000800;
    private const uint DiscoveryClass = 0x534cffff;

    public static bool TryParse(ReadOnlySpan<byte> packet, out DiscoveredRadio radio)
    {
        radio = new DiscoveredRadio(string.Empty, 0, string.Empty, string.Empty, string.Empty);
        if (packet.Length < 16)
        {
            return false;
        }

        var header = BinaryPrimitives.ReadUInt32BigEndian(packet);
        var packetSize = checked((int)(header & 0xffff) * 4);
        if ((header >> 28) != ExtendedDataWithStream
            || (header & ClassPresent) == 0
            || packetSize > packet.Length
            || packetSize < 16
            || BinaryPrimitives.ReadUInt32BigEndian(packet[4..]) != DiscoveryStream
            || BinaryPrimitives.ReadUInt32BigEndian(packet[12..]) != DiscoveryClass)
        {
            return false;
        }

        var payloadOffset = 16;
        if (((header >> 22) & 0x3) != 0)
        {
            payloadOffset += 4;
        }
        if (((header >> 20) & 0x3) != 0)
        {
            payloadOffset += 8;
        }
        if (payloadOffset >= packetSize)
        {
            return false;
        }

        var payloadBytes = packet[payloadOffset..packetSize];
        while (!payloadBytes.IsEmpty && payloadBytes[^1] == 0)
        {
            payloadBytes = payloadBytes[..^1];
        }
        if (payloadBytes.IsEmpty || payloadBytes.Contains((byte)0))
        {
            return false;
        }

        var fields = ParseFields(Encoding.ASCII.GetString(payloadBytes));
        if (!fields.TryGetValue("serial", out var serial)
            || !fields.TryGetValue("ip", out var host)
            || !fields.TryGetValue("port", out var portText)
            || !IPAddress.TryParse(host, out var address)
            || address.AddressFamily != AddressFamily.InterNetwork
            || !int.TryParse(portText, out var port)
            || port is < 1 or > 65535)
        {
            return false;
        }

        fields.TryGetValue("model", out var model);
        if (!fields.TryGetValue("nickname", out var nickname))
        {
            fields.TryGetValue("name", out nickname);
        }
        radio = new DiscoveredRadio(
            host,
            port,
            serial,
            model ?? string.Empty,
            nickname ?? string.Empty);
        return true;
    }

    public static bool Matches(RadioConnectionOptions options, DiscoveredRadio radio) =>
        options.Mode == RadioConnectionMode.Discovery
        && (string.IsNullOrWhiteSpace(options.Serial)
            || string.Equals(options.Serial.Trim(), radio.Serial, StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrWhiteSpace(options.DiscoveryIp)
            || string.Equals(options.DiscoveryIp.Trim(), radio.Host, StringComparison.Ordinal));

    private static Dictionary<string, string> ParseFields(string payload)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in payload.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = token.IndexOf('=');
            if (separator > 0 && separator < token.Length - 1)
            {
                fields[token[..separator]] = token[(separator + 1)..];
            }
        }
        return fields;
    }
}
