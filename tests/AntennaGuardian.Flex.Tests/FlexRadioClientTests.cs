using System.Net;
using System.Net.Sockets;
using System.Text;
using AntennaGuardian.Core;
using AntennaGuardian.Flex;

namespace AntennaGuardian.Flex.Tests;

public sealed class FlexRadioClientTests
{
    [Fact]
    public async Task ConcurrentDisconnectsTreatDuplicateInterlockRemovalAsNonfatal()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = RunFakeRadioAsync(listener, timeout.Token);
        var client = new FlexRadioClient(port);

        try
        {
            await client.ConnectAsync(IPAddress.Loopback.ToString(), timeout.Token);
            await client.ExecuteAsync(
                new RegisterInterlock(["ANT1", "ANT2"]),
                timeout.Token);

            var first = client.DisconnectAsync(timeout.Token);
            var second = client.DisconnectAsync(timeout.Token);

            await Task.WhenAll(first, second);
            await server;
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    private static async Task RunFakeRadioAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        using var connection = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n",
        };

        for (var index = 0; index < 4; index++)
        {
            var command = await ReadCommandAsync(reader, cancellationToken);
            await writer.WriteLineAsync($"R{command.Sequence}|0|");
        }

        var register = await ReadCommandAsync(reader, cancellationToken);
        Assert.StartsWith("interlock create ", register.Command);
        await writer.WriteLineAsync($"R{register.Sequence}|0|1");

        var firstRemove = await ReadCommandAsync(reader, cancellationToken);
        Assert.Equal("interlock remove 1", firstRemove.Command);

        using var duplicateTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        duplicateTimeout.CancelAfter(TimeSpan.FromMilliseconds(200));
        (int Sequence, string Command)? secondRemove = null;
        try
        {
            secondRemove = await ReadCommandAsync(reader, duplicateTimeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        await writer.WriteLineAsync($"R{firstRemove.Sequence}|0|");
        if (secondRemove is not null)
        {
            Assert.Equal("interlock remove 1", secondRemove.Value.Command);
            await writer.WriteLineAsync($"R{secondRemove.Value.Sequence}|5000008D|");
        }
    }

    private static async Task<(int Sequence, string Command)> ReadCommandAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var line = await reader.ReadLineAsync(cancellationToken);
        Assert.NotNull(line);
        Assert.StartsWith("C", line);
        var separator = line.IndexOf('|');
        Assert.True(separator > 1);
        return (int.Parse(line.AsSpan(1, separator - 1)), line[(separator + 1)..]);
    }
}
