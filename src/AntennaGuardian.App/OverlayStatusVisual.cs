using AntennaGuardian.Core;

namespace AntennaGuardian.App;

internal sealed record OverlayStatusVisual(string Label, byte Red, byte Green, byte Blue);

internal static class OverlayStatusVisuals
{
    private static readonly OverlayStatusVisual Neutral = new("OFFLINE", 111, 120, 128);
    private static readonly OverlayStatusVisual Warning = new("CONNECTING", 240, 180, 77);
    private static readonly OverlayStatusVisual Blocked = new("TX BLOCKED", 239, 91, 98);
    private static readonly OverlayStatusVisual Allowed = new("TX ALLOWED", 50, 196, 129);
    private static readonly OverlayStatusVisual Transmitting = new("TRANSMITTING", 50, 196, 129);
    private static readonly OverlayStatusVisual Fault = new("FAULT", 239, 91, 98);
    private static readonly OverlayStatusVisual Protected = new("PROTECTED", 50, 196, 129);
    private static readonly OverlayStatusVisual ProtectedBlocked = new("PROTECTED / BLOCKED", 239, 91, 98);

    public static OverlayStatusVisual For(GuardianStatus status) => status.State switch
    {
        ProtectionState.Offline => Neutral,
        ProtectionState.Registering => Warning,
        ProtectionState.Blocking => Blocked,
        ProtectionState.Allowing => Allowed,
        ProtectionState.Transmitting => Transmitting,
        ProtectionState.Faulted => Fault,
        ProtectionState.Armed => Protected,
        _ when status.Decision.IsAllowed => Protected,
        _ => ProtectedBlocked,
    };
}
