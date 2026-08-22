using System.Globalization;
using AntennaGuardian.Core;

namespace AntennaGuardian.Flex;

public sealed class FlexProtocol
{
    private double? _frequencyMhz;
    private string? _txAntenna;
    private TxContext? _lastPublishedContext;

    public IReadOnlyList<RadioEvent> Feed(string line)
    {
        var separator = line.IndexOf('|');
        if (separator < 0 || separator == line.Length - 1)
        {
            return [];
        }

        var payload = line[(separator + 1)..];
        if (payload.StartsWith("interlock ", StringComparison.Ordinal))
        {
            var interlockFields = ParseFields(payload.AsSpan("interlock ".Length));
            if (!interlockFields.TryGetValue("state", out var state))
            {
                return [];
            }

            return state switch
            {
                "PTT_REQUESTED" => [new PttRequested()],
                "UNKEY_REQUESTED" => [new UnkeyRequested()],
                "TRANSMITTING" => [new RadioTransmitting()],
                _ => [],
            };
        }

        if (!payload.StartsWith("transmit ", StringComparison.Ordinal))
        {
            return [];
        }

        var fields = ParseFields(payload.AsSpan("transmit ".Length));
        if (fields.TryGetValue("freq", out var frequencyText)
            && double.TryParse(
                frequencyText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var frequency))
        {
            _frequencyMhz = frequency;
        }

        if (fields.TryGetValue("tx_antenna", out var antenna))
        {
            _txAntenna = antenna;
        }

        if (_frequencyMhz is null || string.IsNullOrWhiteSpace(_txAntenna))
        {
            return [];
        }

        var context = new TxContext(_frequencyMhz, _txAntenna);
        if (context == _lastPublishedContext)
        {
            return [];
        }

        _lastPublishedContext = context;
        return [new TxContextChanged(context)];
    }

    private static Dictionary<string, string> ParseFields(ReadOnlySpan<char> payload)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in payload.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = token.IndexOf('=');
            if (equals > 0 && equals < token.Length - 1)
            {
                fields[token[..equals]] = token[(equals + 1)..];
            }
        }

        return fields;
    }
}
