using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using AntennaGuardian.Core;

namespace AntennaGuardian.Flex;

public sealed record FlexResponse(int Sequence, uint Code, string Body)
{
    public bool IsSuccess => Code == 0;
}

public sealed class FlexRadioClient : IAsyncDisposable
{
    private const int FlexPort = 4992;
    private readonly int _port;
    private readonly FlexProtocol _protocol = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<FlexResponse>> _pending = new();
    private readonly SemaphoreSlim _disconnectGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private Task? _readerTask;
    private int _sequence;
    private string? _interlockId;
    private bool _intentionalDisconnect;
    private bool _disconnected;

    public FlexRadioClient() : this(FlexPort)
    {
    }

    internal FlexRadioClient(int port)
    {
        _port = port;
    }

    public event Action<RadioEvent>? RadioEventReceived;
    public event Action<string>? Activity;

    public Task Completion => _readerTask ?? Task.CompletedTask;
    public bool IsConnected => _tcpClient?.Connected == true && _stream is not null;

    public async Task ConnectAsync(string host, CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return;
        }

        _intentionalDisconnect = false;
        _tcpClient = new TcpClient
        {
            NoDelay = true,
        };
        _tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        await _tcpClient.ConnectAsync(host, _port, cancellationToken);
        _stream = _tcpClient.GetStream();
        _readerTask = ReadLoopAsync(_lifetime.Token);

        await RequireSuccessAsync("name AntennaGuardian", cancellationToken);
        await RequireSuccessAsync("sub radio all", cancellationToken);
        await RequireSuccessAsync("sub slice all", cancellationToken);
        await RequireSuccessAsync("sub tx all", cancellationToken);
        RadioEventReceived?.Invoke(new RadioConnected());
    }

    public async Task ExecuteAsync(GuardianCommand command, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case RegisterInterlock register:
            {
                var antennas = string.Join(',', register.Antennas);
                var response = await RequireSuccessAsync(
                    "interlock create type=ANT name=AntennaGuardian "
                    + $"serial=desktop valid_antennas={antennas}",
                    cancellationToken);
                var interlockId = response.Body.Split('|', 2)[0].Trim();
                if (string.IsNullOrWhiteSpace(interlockId))
                {
                    throw new InvalidOperationException("Radio created an interlock without returning its ID.");
                }

                _interlockId = interlockId;
                RadioEventReceived?.Invoke(new InterlockRegistered(interlockId));
                break;
            }

            case SetInterlockNotReady notReady:
                await RequireSuccessAsync(
                    $"interlock not_ready {notReady.InterlockId}",
                    cancellationToken);
                break;

            case SetInterlockReady ready:
                await RequireSuccessAsync(
                    $"interlock ready {ready.InterlockId}",
                    cancellationToken);
                break;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _disconnectGate.WaitAsync(cancellationToken);
        try
        {
            if (_disconnected)
            {
                return;
            }

            _disconnected = true;
            _intentionalDisconnect = true;
            var interlockId = _interlockId;
            _interlockId = null;
            try
            {
                if (interlockId is not null && IsConnected)
                {
                    await RequireSuccessAsync(
                        $"interlock remove {interlockId}",
                        cancellationToken).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Activity?.Invoke($"Interlock cleanup was not confirmed: {error.Message}");
            }
            finally
            {
                _lifetime.Cancel();
                _stream?.Close();
                _tcpClient?.Close();
                if (_readerTask is not null)
                {
                    try
                    {
                        await _readerTask;
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
        }
        finally
        {
            _disconnectGate.Release();
        }
    }

    private async Task<FlexResponse> RequireSuccessAsync(
        string command,
        CancellationToken cancellationToken)
    {
        var response = await SendCommandAsync(command, cancellationToken);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Radio rejected '{command}' with 0x{response.Code:X8}.");
        }

        return response;
    }

    private async Task<FlexResponse> SendCommandAsync(
        string command,
        CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException("Radio is not connected.");
        var sequence = Interlocked.Increment(ref _sequence);
        var completion = new TaskCompletionSource<FlexResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(sequence, completion))
        {
            throw new InvalidOperationException("Duplicate Flex command sequence.");
        }

        var frame = $"C{sequence}|{command}\n";
        var bytes = Encoding.ASCII.GetBytes(frame);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            Activity?.Invoke($"> {command}");
        }
        finally
        {
            _writeGate.Release();
        }

        try
        {
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(4), cancellationToken);
        }
        finally
        {
            _pending.TryRemove(sequence, out _);
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(
                _stream ?? throw new InvalidOperationException("Radio stream is unavailable."),
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    throw new IOException("Radio closed the TCP connection.");
                }

                line = line.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                Activity?.Invoke($"< {line}");
                if (TryParseResponse(line, out var response))
                {
                    if (_pending.TryGetValue(response.Sequence, out var completion))
                    {
                        completion.TrySetResult(response);
                    }
                    continue;
                }

                foreach (var radioEvent in _protocol.Feed(line))
                {
                    RadioEventReceived?.Invoke(radioEvent);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or SocketException)
        {
            Activity?.Invoke(error.Message);
            foreach (var completion in _pending.Values)
            {
                completion.TrySetException(error);
            }
        }
        finally
        {
            if (!_intentionalDisconnect)
            {
                RadioEventReceived?.Invoke(new RadioDisconnected("Radio connection lost"));
            }
        }
    }

    private static bool TryParseResponse(string line, out FlexResponse response)
    {
        response = new FlexResponse(0, 0, string.Empty);
        if (line.Length < 4 || line[0] != 'R')
        {
            return false;
        }

        var parts = line[1..].Split('|', 3);
        if (parts.Length < 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
            || !uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
        {
            return false;
        }

        response = new FlexResponse(sequence, code, parts.Length == 3 ? parts[2] : string.Empty);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _disconnectGate.Dispose();
        _writeGate.Dispose();
        _lifetime.Dispose();
    }
}
