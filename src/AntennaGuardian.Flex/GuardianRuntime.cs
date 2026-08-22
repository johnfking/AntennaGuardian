using AntennaGuardian.Core;

namespace AntennaGuardian.Flex;

public sealed class GuardianRuntime : IAsyncDisposable
{
    private readonly string _radioHost;
    private readonly GuardianController _controller;
    private readonly SemaphoreSlim _eventGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private FlexRadioClient? _client;
    private Task? _runTask;

    public GuardianRuntime(
        string radioHost,
        AntennaPolicy policy,
        IReadOnlyList<string> interlockAntennas)
    {
        _radioHost = radioHost;
        _controller = new GuardianController(new PolicyEngine(), policy, interlockAntennas);
    }

    public event Action<GuardianStatus>? StatusChanged;
    public event Action<string>? Activity;

    public void Start()
    {
        _runTask ??= RunAsync(_lifetime.Token);
    }

    public async Task UpdatePolicyAsync(AntennaPolicy policy)
    {
        await HandleEventAsync(new PolicyChanged(policy), _lifetime.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var client = new FlexRadioClient();
            _client = client;
            client.Activity += OnActivity;
            client.RadioEventReceived += OnRadioEvent;
            try
            {
                Activity?.Invoke($"Connecting to radio at {_radioHost}...");
                await client.ConnectAsync(_radioHost, cancellationToken);
                await client.Completion.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                await HandleEventAsync(
                    new RadioDisconnected(error.Message),
                    CancellationToken.None);
                Activity?.Invoke(error.Message);
            }
            finally
            {
                client.RadioEventReceived -= OnRadioEvent;
                client.Activity -= OnActivity;
                _client = null;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void OnRadioEvent(RadioEvent radioEvent)
    {
        _ = HandleEventAsync(radioEvent, _lifetime.Token);
    }

    private void OnActivity(string message)
    {
        Activity?.Invoke(message);
    }

    private async Task HandleEventAsync(RadioEvent radioEvent, CancellationToken cancellationToken)
    {
        await _eventGate.WaitAsync(cancellationToken);
        try
        {
            var result = _controller.Handle(radioEvent);
            StatusChanged?.Invoke(result.Status);
            var client = _client;
            if (client is null)
            {
                return;
            }

            foreach (var command in result.Commands)
            {
                await client.ExecuteAsync(command, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Activity?.Invoke(error.Message);
            StatusChanged?.Invoke(new GuardianStatus(
                ProtectionState.Faulted,
                new TxContext(null, null),
                new PolicyEngine().Evaluate(new TxContext(null, null), AntennaPolicy.Empty),
                null,
                error.Message));
        }
        finally
        {
            _eventGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_client is not null)
        {
            await _client.DisconnectAsync();
        }
        if (_runTask is not null)
        {
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        _eventGate.Dispose();
        _lifetime.Dispose();
    }
}
