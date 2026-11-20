using System.Text.Json;

namespace PufferHelper;

/// <summary>Running API spend across sessions, kept in %AppData%\PufferHelper\usage.json.</summary>
internal sealed class SpendLedger
{
    public int Requests { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }

    /// <summary>Estimated dollars for the requests whose model price is known.</summary>
    public decimal Dollars { get; set; }

    /// <summary>Requests on a model missing from <see cref="Pricing"/>; their cost isn't in <see cref="Dollars"/>.</summary>
    public int UnpricedRequests { get; set; }

    public DateTime Since { get; set; } = DateTime.Now;

    public static string FilePath => Path.Combine(AppSettings.FolderPath, "usage.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static SpendLedger Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<SpendLedger>(File.ReadAllText(FilePath), JsonOptions) ?? new SpendLedger();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged counter file just starts the count again.
        }
        return new SpendLedger();
    }

    public void Add(RequestUsage usage)
    {
        Requests++;
        InputTokens += usage.InputTokens;
        OutputTokens += usage.OutputTokens;
        if (usage.Dollars is decimal dollars)
            Dollars += dollars;
        else
            UnpricedRequests++;
    }

    public bool TrySave()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.FolderPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
