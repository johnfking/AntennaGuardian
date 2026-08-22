using AntennaGuardian.Core;

namespace AntennaGuardian.Core.Tests;

public sealed class GuardianControllerTests
{
    [Fact]
    public void BlockedPttNeverProducesReadyCommand()
    {
        var controller = new GuardianController(
            new PolicyEngine(),
            AntennaPolicy.FromAllowed(("ANT1", "20m")),
            ["ANT1", "ANT2"]);

        controller.Handle(new InterlockRegistered("1"));
        controller.Handle(new TxContextChanged(new TxContext(14.074, "ANT2")));
        var result = controller.Handle(new PttRequested());

        Assert.Equal(ProtectionState.Blocking, result.Status.State);
        Assert.Equal(DecisionReason.CombinationNotAllowed, result.Status.Decision.Reason);
        Assert.DoesNotContain(result.Commands, command => command is SetInterlockReady);
    }

    [Fact]
    public void AllowedPttProducesReadyForRegisteredInterlock()
    {
        var controller = new GuardianController(
            new PolicyEngine(),
            AntennaPolicy.FromAllowed(("ANT1", "20m")),
            ["ANT1", "ANT2"]);

        controller.Handle(new InterlockRegistered("A4"));
        controller.Handle(new TxContextChanged(new TxContext(14.074, "ANT1")));
        var result = controller.Handle(new PttRequested());

        Assert.Equal(ProtectionState.Allowing, result.Status.State);
        Assert.Contains(new SetInterlockReady("A4"), result.Commands);
    }

    [Fact]
    public void RadioConnectionStartsInterlockRegistration()
    {
        var controller = new GuardianController(
            new PolicyEngine(),
            AntennaPolicy.Empty,
            ["ANT1", "ANT2"]);

        var result = controller.Handle(new RadioConnected());

        Assert.Equal(ProtectionState.Registering, result.Status.State);
        var command = Assert.IsType<RegisterInterlock>(Assert.Single(result.Commands));
        Assert.Equal(["ANT1", "ANT2"], command.Antennas);
    }

    [Fact]
    public void UnkeyReturnsRegisteredInterlockToNotReady()
    {
        var controller = new GuardianController(
            new PolicyEngine(),
            AntennaPolicy.FromAllowed(("ANT1", "20m")),
            ["ANT1", "ANT2"]);
        controller.Handle(new InterlockRegistered("9"));
        controller.Handle(new TxContextChanged(new TxContext(14.074, "ANT1")));
        controller.Handle(new PttRequested());

        var result = controller.Handle(new UnkeyRequested());

        Assert.Equal(ProtectionState.Armed, result.Status.State);
        Assert.Contains(new SetInterlockNotReady("9"), result.Commands);
    }

    [Fact]
    public void DisconnectClearsRadioStateAndReportsOffline()
    {
        var controller = new GuardianController(
            new PolicyEngine(),
            AntennaPolicy.Empty,
            ["ANT1", "ANT2"]);
        controller.Handle(new InterlockRegistered("9"));
        controller.Handle(new TxContextChanged(new TxContext(14.074, "ANT1")));

        var result = controller.Handle(new RadioDisconnected("Network link lost"));

        Assert.Equal(ProtectionState.Offline, result.Status.State);
        Assert.Null(result.Status.InterlockId);
        Assert.Null(result.Status.Context.FrequencyMhz);
        Assert.Equal(DecisionReason.UnknownFrequency, result.Status.Decision.Reason);
    }

    [Fact]
    public void PolicyChangeImmediatelyReturnsInterlockToNotReady()
    {
        var controller = new GuardianController(
            new PolicyEngine(),
            AntennaPolicy.FromAllowed(("ANT1", "20m")),
            ["ANT1", "ANT2"]);
        controller.Handle(new InterlockRegistered("9"));
        controller.Handle(new TxContextChanged(new TxContext(14.074, "ANT1")));

        var result = controller.Handle(new PolicyChanged(AntennaPolicy.Empty));

        Assert.False(result.Status.Decision.IsAllowed);
        Assert.Contains(new SetInterlockNotReady("9"), result.Commands);
    }

    [Fact]
    public void ContextChangeToBlockedCombinationImmediatelyReturnsInterlockToNotReady()
    {
        var controller = new GuardianController(
            new PolicyEngine(),
            AntennaPolicy.FromAllowed(("ANT1", "20m")),
            ["ANT1", "ANT2"]);
        controller.Handle(new InterlockRegistered("9"));
        controller.Handle(new TxContextChanged(new TxContext(14.074, "ANT1")));

        var result = controller.Handle(new TxContextChanged(new TxContext(14.074, "ANT2")));

        Assert.Equal(ProtectionState.Blocking, result.Status.State);
        Assert.Contains(new SetInterlockNotReady("9"), result.Commands);
    }

    [Fact]
    public void AllowedTransmissionIsReportedAsTransmitting()
    {
        var controller = new GuardianController(
            new PolicyEngine(),
            AntennaPolicy.FromAllowed(("ANT1", "20m")),
            ["ANT1", "ANT2"]);
        controller.Handle(new InterlockRegistered("9"));
        controller.Handle(new TxContextChanged(new TxContext(14.074, "ANT1")));
        controller.Handle(new PttRequested());

        var result = controller.Handle(new RadioTransmitting());

        Assert.Equal(ProtectionState.Transmitting, result.Status.State);
        Assert.Empty(result.Commands);
    }

    [Fact]
    public void UnexpectedBlockedTransmissionFaultsAndReassertsNotReady()
    {
        var controller = new GuardianController(
            new PolicyEngine(),
            AntennaPolicy.Empty,
            ["ANT1", "ANT2"]);
        controller.Handle(new InterlockRegistered("9"));
        controller.Handle(new TxContextChanged(new TxContext(14.074, "ANT1")));

        var result = controller.Handle(new RadioTransmitting());

        Assert.Equal(ProtectionState.Faulted, result.Status.State);
        Assert.Contains(new SetInterlockNotReady("9"), result.Commands);
    }
}
