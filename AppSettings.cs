using System.Text.Encodings.Web;
using System.Text.Json;

namespace PufferHelper;

/// <summary>Settings kept in %AppData%\PufferHelper\settings.json. Edit the file while the helper is closed.</summary>
internal sealed class AppSettings
{
    /// <summary>Claude model id. "claude-haiku-5-5" is faster and much cheaper.</summary>
    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>How hard the model thinks: low, medium, high or max. Low keeps answers coming in a few seconds.</summary>
    public string Effort { get; set; } = "low";

    /// <summary>How many candidate answers to ask for per category.</summary>
    public int AnswerCount { get; set; } = 25;

    /// <summary>Hide the overlay from screen sharing, recording and screenshots (Windows 10 2004 or later).</summary>
    public bool HideFromScreenCapture { get; set; } = true;

    public string HotkeyAsk { get; set; } = "Ctrl+Alt+Q";
    public string HotkeyRefresh { get; set; } = "Ctrl+Alt+W";
    public string HotkeyPickRegion { get; set; } = "Ctrl+Alt+R";
    public string HotkeyToggle { get; set; } = "Ctrl+Alt+H";

    /// <summary>The part of the screen the game shows its category in, in screen pixels. Null = the whole screen.</summary>
    public RegionSetting? Region { get; set; }

    public int? OverlayX { get; set; }
    public int? OverlayY { get; set; }

    public static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PufferHelper");

    public static string FilePath => Path.Combine(FolderPath, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Keep "Ctrl+Alt+Q" readable in the file instead of "Ctrl+Alt+Q".
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppSettings Load(out string? error)
    {
        error = null;
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep the broken file as it is so the user can fix it; run on defaults meanwhile.
            error = $"settings.json could not be read ({ex.Message}); using defaults.";
            return new AppSettings();
        }

        var defaults = new AppSettings();
        defaults.TrySave();
        return defaults;
    }

    public bool TrySave()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

internal sealed record RegionSetting(int X, int Y, int Width, int Height)
{
    public Rectangle ToRectangle() => new(X, Y, Width, Height);

    public static RegionSetting From(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}
