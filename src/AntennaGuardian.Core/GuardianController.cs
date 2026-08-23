namespace AntennaGuardian.Core;

public enum ProtectionState
{
    Offline,
    Registering,
    Armed,
    Allowing,
    Blocking,
    Transmitting,
    Faulted,
}

public abstract record RadioEvent;
public sealed record RadioConnected : RadioEvent;
public sealed record RadioDisconnected(string Reason) : RadioEvent;
public sealed record RadioIdentityUpdated(string Nickname) : RadioEvent;
public sealed record InterlockRegistered(string InterlockId) : RadioEvent;
public sealed record TxContextChanged(TxContext Context) : RadioEvent;
public sealed record PttRequested : RadioEvent;
public sealed record UnkeyRequested : RadioEvent;
public sealed record RadioTransmitting : RadioEvent;
public sealed record PolicyChanged(AntennaPolicy Policy) : RadioEvent;

public abstract record GuardianCommand;
public sealed record RegisterInterlock(IReadOnlyList<string> Antennas) : GuardianCommand;
public sealed record SetInterlockNotReady(string InterlockId) : GuardianCommand;
public sealed record SetInterlockReady(string InterlockId) : GuardianCommand;

public sealed record GuardianStatus(
    ProtectionState State,
    TxContext Context,
    PolicyDecision Decision,
    string? InterlockId,
    string Message);

public sealed record ControllerResult(
    GuardianStatus Status,
    IReadOnlyList<GuardianCommand> Commands);

public sealed class GuardianController
{
    private readonly PolicyEngine _engine;
    private readonly IReadOnlyList<string> _interlockAntennas;
    private AntennaPolicy _policy;
    private TxContext _context = new(null, null);
    private string? _interlockId;
    private GuardianStatus _status;

    public GuardianController(
        PolicyEngine engine,
        AntennaPolicy policy,
        IReadOnlyList<string> interlockAntennas)
    {
        _engine = engine;
        _policy = policy;
        _interlockAntennas = interlockAntennas;
        var decision = _engine.Evaluate(_context, _policy);
        _status = new GuardianStatus(
            ProtectionState.Offline,
            _context,
            decision,
            null,
            "Protection is offline.");
    }

    public ControllerResult Handle(RadioEvent radioEvent)
    {
        switch (radioEvent)
        {
            case RadioConnected:
                return SetStatus(
                    ProtectionState.Registering,
                    "Registering antenna interlock.",
                    new RegisterInterlock(_interlockAntennas));

            case RadioDisconnected disconnected:
                _interlockId = null;
                _context = new TxContext(null, null);
                return SetStatus(
                    ProtectionState.Offline,
                    $"Protection is offline: {disconnected.Reason}");

            case InterlockRegistered registered:
                _interlockId = registered.InterlockId;
                return SetStatus(
                    ProtectionState.Armed,
                    "Interlock armed.",
                    new SetInterlockNotReady(registered.InterlockId));

            case TxContextChanged changed:
                _context = changed.Context;
                var contextDecision = _engine.Evaluate(_context, _policy);
                var contextUnavailable = _context.FrequencyMhz is null
                    || string.IsNullOrWhiteSpace(_context.TxAntenna);
                if (_interlockId is not null && contextUnavailable)
                {
                    return SetStatus(
                        ProtectionState.Armed,
                        "Waiting for an active transmit context.",
                        new SetInterlockNotReady(_interlockId));
                }
                if (_interlockId is not null && !contextDecision.IsAllowed)
                {
                    return SetStatus(
                        ProtectionState.Blocking,
                        contextDecision.Message,
                        new SetInterlockNotReady(_interlockId));
                }

                var contextState = _interlockId is not null && _status.State == ProtectionState.Blocking
                    ? ProtectionState.Armed
                    : _status.State;
                return SetStatus(contextState, contextDecision.Message);

            case PolicyChanged changed:
                _policy = changed.Policy;
                return _interlockId is null
                    ? SetStatus(_status.State, _engine.Evaluate(_context, _policy).Message)
                    : SetStatus(
                        ProtectionState.Armed,
                        _engine.Evaluate(_context, _policy).Message,
                        new SetInterlockNotReady(_interlockId));

            case PttRequested:
                var decision = _engine.Evaluate(_context, _policy);
                _status = _status with
                {
                    State = decision.IsAllowed ? ProtectionState.Allowing : ProtectionState.Blocking,
                    Decision = decision,
                    Message = decision.Message,
                };
                var commands = decision.IsAllowed && _interlockId is not null
                    ? new GuardianCommand[] { new SetInterlockReady(_interlockId) }
                    : [];
                return new ControllerResult(_status, commands);

            case UnkeyRequested when _interlockId is not null:
                return SetStatus(
                    ProtectionState.Armed,
                    "Interlock armed.",
                    new SetInterlockNotReady(_interlockId));

            case RadioTransmitting:
                var transmitDecision = _engine.Evaluate(_context, _policy);
                if (!transmitDecision.IsAllowed)
                {
                    return _interlockId is null
                        ? SetStatus(
                            ProtectionState.Faulted,
                            "Radio reports transmission outside the active antenna policy.")
                        : SetStatus(
                            ProtectionState.Faulted,
                            "Radio reports transmission outside the active antenna policy.",
                            new SetInterlockNotReady(_interlockId));
                }

                return SetStatus(ProtectionState.Transmitting, "Transmission allowed.");

            default:
                return new ControllerResult(_status, []);
        }
    }

    private ControllerResult SetStatus(
        ProtectionState state,
        string message,
        params GuardianCommand[] commands)
    {
        var decision = _engine.Evaluate(_context, _policy);
        _status = new GuardianStatus(
            state,
            _context,
            decision,
            _interlockId,
            message);
        return new ControllerResult(_status, commands);
    }
}
