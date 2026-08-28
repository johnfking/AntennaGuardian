using AntennaGuardian.Core;

namespace AntennaGuardian.Flex;

public sealed class GuardianRuntime : IAsyncDisposable
{
    private readonly RadioConnectionOptions _connectionOptions;
    private readonly IFlexRadioDiscovery _discovery;
    private readonly GuardianController _controller;
    private readonly SemaphoreSlim _eventGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private FlexRadioClient? _client;
    private Task? _runTask;

    public GuardianRuntime(
        RadioConnectionOptions connectionOptions,
        AntennaPolicy policy,
        IReadOnlyList<string> interlockAntennas)
        : this(connectionOptions, policy, interlockAntennas, new FlexRadioDiscovery())
    {
    }

    internal GuardianRuntime(
        RadioConnectionOptions connectionOptions,
        AntennaPolicy policy,
        IReadOnlyList<string> interlockAntennas,
        IFlexRadioDiscovery discovery)
    {
        _connectionOptions = connectionOptions;
        _discovery = discovery;
        _controller = new GuardianController(new PolicyEngine(), policy, interlockAntennas);
    }

    public event Action<GuardianStatus>? StatusChanged;
    public event Action<string>? Activity;
    public event Action<string>? RadioIdentityChanged;
    public event Action<DiscoveredRadio>? RadioEndpointChanged;

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
            DiscoveredRadio endpoint;
            try
            {
                endpoint = await ResolveEndpointAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                await ReportDisconnectAsync(error.Message);
                await DelayAfterFailureAsync(cancellationToken);
                continue;
            }

            await using var client = new FlexRadioClient(endpoint.Port);
            _client = client;
            client.Activity += OnActivity;
            client.RadioEventReceived += OnRadioEvent;
            try
            {
                RadioEndpointChanged?.Invoke(endpoint);
                if (!string.IsNullOrWhiteSpace(endpoint.Nickname))
                {
                    RadioIdentityChanged?.Invoke(endpoint.Nickname);
                }
                Activity?.Invoke($"Connecting to radio at {endpoint.Host}:{endpoint.Port}...");
                await client.ConnectAsync(endpoint.Host, cancellationToken);
                await client.Completion.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                await ReportDisconnectAsync(error.Message);
            }
            finally
            {
                client.RadioEventReceived -= OnRadioEvent;
                client.Activity -= OnActivity;
                _client = null;
            }

            if (_connectionOptions.Mode == RadioConnectionMode.Direct)
            {
                await DelayAfterFailureAsync(cancellationToken);
            }
        }
    }

    private async Task<DiscoveredRadio> ResolveEndpointAsync(CancellationToken cancellationToken)
    {
        if (_connectionOptions.Mode == RadioConnectionMode.Direct)
        {
            return new DiscoveredRadio(
                _connectionOptions.DirectHost,
                4992,
                string.Empty,
                string.Empty,
                string.Empty);
        }

        var selector = FormatSelector(_connectionOptions);
        Activity?.Invoke($"Waiting for Flex discovery broadcast matching {selector}...");
        return await _discovery.WaitForMatchAsync(_connectionOptions, cancellationToken);
    }

    private async Task ReportDisconnectAsync(string message)
    {
        await HandleEventAsync(new RadioDisconnected(message), CancellationToken.None);
        Activity?.Invoke(message);
    }

    private static string FormatSelector(RadioConnectionOptions options)
    {
        var selectors = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.Serial))
        {
            selectors.Add($"serial {options.Serial.Trim()}");
        }
        if (!string.IsNullOrWhiteSpace(options.DiscoveryIp))
        {
            selectors.Add($"IP {options.DiscoveryIp.Trim()}");
        }
        return string.Join(" and ", selectors);
    }

    private static async Task DelayAfterFailureAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnRadioEvent(RadioEvent radioEvent)
    {
        if (radioEvent is RadioIdentityUpdated identity)
        {
            RadioIdentityChanged?.Invoke(identity.Nickname);
            return;
        }

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
