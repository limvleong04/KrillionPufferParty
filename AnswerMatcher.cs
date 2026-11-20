using System.Globalization;
using System.Text;

namespace PufferHelper;

/// <summary>Loose text comparison for answers: case, accents, punctuation and simple plurals don't matter.</summary>
internal static class AnswerMatcher
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var sb = new StringBuilder(text.Length);
        bool lastWasSpace = true;
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue; // drop accents: "Pokémon" == "Pokemon"
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasSpace = false;
            }
            else if (c is '\'' or '’')
            {
                // "Brazil's" == "Brazils", "rock 'n' roll" == "rock n roll"
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>True when two answers are the same thing for the game's purposes.</summary>
    public static bool SameAnswer(string a, string b)
    {
        string x = Squash(Normalize(a)), y = Squash(Normalize(b));
        if (x.Length == 0 || y.Length == 0)
            return false;
        return x == y || Singular(x) == Singular(y);
    }

    /// <summary>
    /// Which of <paramref name="candidates"/> appear in text read off the screen (answers other players already typed).
    /// Words that are part of the category title itself are ignored, so the title doesn't cross off its own answers.
    /// </summary>
    public static List<string> FindOnScreen(IEnumerable<string> candidates, string screenText, string category)
    {
        string text = " " + Normalize(screenText) + " ";
        string squashedText = Squash(text);
        string categoryText = " " + Normalize(category) + " ";

        var found = new List<string>();
        foreach (string candidate in candidates)
        {
            string n = Normalize(candidate);
            if (n.Length < 2 || categoryText.Contains(" " + n + " ", StringComparison.Ordinal))
                continue;

            bool seen = text.Contains(" " + n + " ", StringComparison.Ordinal)
                        || text.Contains(" " + Singular(n) + " ", StringComparison.Ordinal) && Singular(n).Length >= 3
                        // OCR sometimes drops or adds spaces inside longer names ("NewZealand").
                        || n.Length >= 6 && squashedText.Contains(Squash(n), StringComparison.Ordinal);
            if (seen)
                found.Add(candidate);
        }
        return found;
    }

    private static string Squash(string normalized) => normalized.Replace(" ", "", StringComparison.Ordinal);

    private static string Singular(string word)
    {
        if (word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal))
            return word[..^3] + "y";
        if (word.Length > 4 && (word.EndsWith("ches", StringComparison.Ordinal) || word.EndsWith("shes", StringComparison.Ordinal)
                                || word.EndsWith("xes", StringComparison.Ordinal) || word.EndsWith("sses", StringComparison.Ordinal)))
            return word[..^2];
        if (word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal))
            return word[..^1];
        return word;
    }
}
