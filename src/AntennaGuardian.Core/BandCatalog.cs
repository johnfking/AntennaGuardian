namespace AntennaGuardian.Core;

public sealed record BandDefinition(string Name, double LowerMhz, double UpperMhz);

public static class BandCatalog
{
    public static IReadOnlyList<BandDefinition> NativeBands { get; } =
    [
        new("160m", 1.8, 2.0),
        new("80m", 3.5, 4.0),
        new("60m", 5.25, 5.45),
        new("40m", 7.0, 7.3),
        new("30m", 10.1, 10.15),
        new("20m", 14.0, 14.35),
        new("17m", 18.068, 18.168),
        new("15m", 21.0, 21.45),
        new("12m", 24.89, 24.99),
        new("10m", 28.0, 29.7),
        new("6m", 50.0, 54.0),
    ];

    public static BandDefinition? Find(double frequencyMhz) =>
        NativeBands.FirstOrDefault(band =>
            frequencyMhz >= band.LowerMhz && frequencyMhz <= band.UpperMhz);
}
