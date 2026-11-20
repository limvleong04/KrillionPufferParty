namespace PufferHelper;

/// <summary>
/// Claude API list prices (Anthropic first-party, US dollars per million tokens, as of November 2026).
/// Used only for the cost readout; platform.claude.com > Usage has the exact figures. Update here if prices change.
/// </summary>
internal static class Pricing
{
    private static readonly Dictionary<string, (decimal Input, decimal Output)> PerMillionTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-fable-5-2"] = (8m, 40m),
        ["claude-fable-5-1"] = (10m, 50m),
        ["claude-fable-5"] = (10m, 50m),
        ["claude-opus-5-5"] = (4m, 20m),
        ["claude-opus-5"] = (5m, 25m),
        ["claude-opus-4-8"] = (5m, 25m),
        ["claude-opus-4-7"] = (5m, 25m),
        ["claude-opus-4-6"] = (5m, 25m),
        ["claude-sonnet-5-5"] = (2m, 10m),
        ["claude-sonnet-5"] = (2m, 10m),
        ["claude-sonnet-4-6"] = (3m, 15m),
        ["claude-haiku-5-5"] = (0.10m, 0.50m),
        ["claude-haiku-4-5"] = (1m, 5m),
    };

    /// <summary>Dollar cost of one request, or null when the model's price isn't in the table.</summary>
    public static decimal? Cost(string model, long inputTokens, long outputTokens) =>
        PerMillionTokens.TryGetValue(model.Trim(), out var price)
            ? (inputTokens * price.Input + outputTokens * price.Output) / 1_000_000m
            : null;

    /// <summary>"$1.23", "43¢", "1.4¢" or "0.03¢": enough precision to be useful at every size.</summary>
    public static string Format(decimal dollars) => dollars switch
    {
        <= 0m => "0¢",
        >= 1m => $"${dollars:0.00}",
        >= 0.10m => $"{dollars * 100m:0}¢",
        >= 0.001m => $"{dollars * 100m:0.0}¢",
        _ => $"{dollars * 100m:0.00}¢",
    };
}

/// <summary>Tokens and estimated cost of one API request.</summary>
internal readonly record struct RequestUsage(string Model, long InputTokens, long OutputTokens, decimal? Dollars);
