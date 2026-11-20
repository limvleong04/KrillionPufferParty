using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace PufferHelper;

internal sealed record AnswerResult(string Category, IReadOnlyList<string> AlreadyPlayed, IReadOnlyList<string> Answers);

/// <summary>Sends one screenshot to Claude and gets back the category and a ranked list of answers.</summary>
internal sealed class AnswerService
{
    private const string SystemPrompt = """
        You help one player in a fast online party word game. The screen shows a category (for example "Dog breeds" or "Things in a kitchen"). Players take turns typing one answer that belongs to the category, and an answer that has already been played cannot be used again. The game checks each typed answer against a curated list built from dictionaries and encyclopedias, and forgives small spelling slips. The player has only a few seconds per turn, so an answer the list is sure to contain beats a clever one.

        From the screenshot:
        1. Read the category exactly as shown. If no category is visible, return an empty category and empty lists.
        2. List every answer you can see that has already been played this round, written as shown.
        3. Suggest answers for the player to type:
           - each one clearly belongs to the category and is the kind of thing a reference list would contain;
           - safest, most widely known answers first;
           - standard spelling, and the short name a person would actually type: no explanations, no leading "the", no parentheses;
           - leave out anything already played or listed as already used, and never give the same answer twice in another form (singular/plural, abbreviation, alternate spelling).
        """;

    private static readonly Dictionary<string, JsonElement> Schema = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            category = new { type = "string", description = "The category exactly as shown, or empty if none is visible." },
            already_played = new { type = "array", items = new { type = "string" }, description = "Answers already played this round that are visible on screen." },
            answers = new { type = "array", items = new { type = "string" }, description = "Suggested answers, safest first." },
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "category", "already_played", "answers" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };

    private AnthropicClient? _client;

    public async Task<AnswerResult> AskAsync(byte[] png, IReadOnlyCollection<string> knownUsed, AppSettings settings, CancellationToken ct)
    {
        // Created on first use so a missing API key shows up as a status message rather than a crash at start-up.
        // The client reads ANTHROPIC_API_KEY from the environment.
        _client ??= new AnthropicClient { MaxRetries = 1, Timeout = TimeSpan.FromSeconds(45) };

        string instruction = $"Suggest {Math.Clamp(settings.AnswerCount, 5, 60)} answers.";
        if (knownUsed.Count > 0)
            instruction += " Already used this round, leave these out: " + string.Join(", ", knownUsed) + ".";

        var content = new List<BetaContentBlockParam>
        {
            new BetaImageBlockParam
            {
                Source = new BetaBase64ImageSource { Data = Convert.ToBase64String(png), MediaType = MediaType.ImagePng },
            },
            new BetaTextBlockParam { Text = instruction },
        };

        var outputConfig = new BetaOutputConfig
        {
            Effort = ParseEffort(settings.Effort),
            Format = new BetaJsonOutputFormat { Schema = Schema },
        };

        // Server-side refusal fallback: if the model declines (a false positive on a harmless category), the API
        // retries on a fallback model inside the same call. Haiku has no server-side fallback, so it is left off there.
        bool useFallback = !settings.Model.Contains("haiku", StringComparison.OrdinalIgnoreCase);

        MessageCreateParams parameters = useFallback
            ? new MessageCreateParams
            {
                Model = settings.Model,
                MaxTokens = 8000,
                System = SystemPrompt,
                OutputConfig = outputConfig,
                Messages = [new BetaUserMessageParam { Content = content }],
                Betas = ["server-side-fallback-2026-07-01"],
                Fallbacks = new Default(),
            }
            : new MessageCreateParams
            {
                Model = settings.Model,
                MaxTokens = 8000,
                System = SystemPrompt,
                OutputConfig = outputConfig,
                Messages = [new BetaUserMessageParam { Content = content }],
            };

        BetaMessage response = await _client.Beta.Messages.Create(parameters, ct);

        if (response.StopReason == "refusal")
            throw new InvalidOperationException("Claude declined to answer this one.");
        if (response.StopReason == "max_tokens")
            throw new InvalidOperationException("The answer was cut off; try again.");

        string json = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
        return Parse(json, knownUsed);
    }

    private static AnswerResult Parse(string json, IReadOnlyCollection<string> knownUsed)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        string category = root.TryGetProperty("category", out var c) ? c.GetString()?.Trim() ?? "" : "";
        List<string> played = ReadList(root, "already_played");

        var answers = new List<string>();
        foreach (string answer in ReadList(root, "answers"))
        {
            bool duplicate = answers.Any(a => AnswerMatcher.SameAnswer(a, answer))
                             || played.Any(p => AnswerMatcher.SameAnswer(p, answer))
                             || knownUsed.Any(u => AnswerMatcher.SameAnswer(u, answer));
            if (!duplicate)
                answers.Add(answer);
        }
        return new AnswerResult(category, played, answers);
    }

    private static List<string> ReadList(JsonElement root, string name)
    {
        var list = new List<string>();
        if (root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in array.EnumerateArray())
            {
                string? text = item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null;
                if (!string.IsNullOrEmpty(text))
                    list.Add(text);
            }
        }
        return list;
    }

    private static Effort ParseEffort(string? effort) => effort?.Trim().ToLowerInvariant() switch
    {
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "max" => Effort.Max,
        _ => Effort.Low,
    };
}
