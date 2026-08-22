namespace AntennaGuardian.Core;

public sealed record TxContext(double? FrequencyMhz, string? TxAntenna);

public enum DecisionReason
{
    Allowed,
    UnknownFrequency,
    UnknownAntenna,
    FrequencyOutsideKnownBands,
    CombinationNotAllowed,
}

public sealed record PolicyDecision(
    bool IsAllowed,
    DecisionReason Reason,
    string Band,
    string Message);

public sealed class AntennaPolicy
{
    private readonly HashSet<(string Antenna, string Band)> _allowed;

    private AntennaPolicy(IEnumerable<(string Antenna, string Band)> allowed)
    {
        _allowed = allowed
            .Select(item => (Normalize(item.Antenna), item.Band))
            .ToHashSet();
    }

    public static AntennaPolicy Empty { get; } = new([]);

    public static AntennaPolicy FromAllowed(params (string Antenna, string Band)[] allowed) =>
        new(allowed);

    public bool IsAllowed(string antenna, string band) =>
        _allowed.Contains((Normalize(antenna), band));

    private static string Normalize(string antenna) => antenna.Trim().ToUpperInvariant();
}

public sealed class PolicyEngine
{
    public PolicyDecision Evaluate(TxContext context, AntennaPolicy policy)
    {
        if (context.FrequencyMhz is null)
        {
            return new PolicyDecision(
                false,
                DecisionReason.UnknownFrequency,
                "Unknown",
                "Transmit frequency is unavailable.");
        }

        if (string.IsNullOrWhiteSpace(context.TxAntenna))
        {
            return new PolicyDecision(
                false,
                DecisionReason.UnknownAntenna,
                "Unknown",
                "Transmit antenna is unavailable.");
        }

        var band = BandCatalog.Find(context.FrequencyMhz.Value);

        if (band is not null && context.TxAntenna is not null)
        {
            var allowed = policy.IsAllowed(context.TxAntenna, band.Name);
            return allowed
                ? new PolicyDecision(
                    true,
                    DecisionReason.Allowed,
                    band.Name,
                    $"{context.TxAntenna} is allowed on {band.Name}.")
                : new PolicyDecision(
                    false,
                    DecisionReason.CombinationNotAllowed,
                    band.Name,
                    $"{context.TxAntenna} is blocked on {band.Name}.");
        }

        return new PolicyDecision(
            false,
            DecisionReason.FrequencyOutsideKnownBands,
            "Unknown",
            $"{context.FrequencyMhz:0.000000} MHz is outside configured native bands.");
    }
}
