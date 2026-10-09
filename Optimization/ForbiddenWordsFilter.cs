using System.Text.Json;
using System.Text.RegularExpressions;

namespace MintChatManager.Optimization;

public sealed class ForbiddenWordsFilter
{
    private static readonly Regex NonWordCharacters = new("[^а-яa-z0-9ё\\s]", RegexOptions.Compiled);
    private static readonly Regex MentionsAndLinks = new(
        "\\[(?:id|club)\\d+\\|[^\\]]+\\]|https?://\\S+|@\\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> WhitelistedPrefixes =
        new(StringComparer.OrdinalIgnoreCase) { "модератор", "модер", "модераци", "модера" };

    private readonly HashSet<string> terms;
    private readonly string[] patterns;

    private ForbiddenWordsFilter(IEnumerable<string> terms, IEnumerable<string> patterns)
    {
        this.terms = new HashSet<string>(
            terms.Where(term => !string.IsNullOrWhiteSpace(term)).Select(term => term.Trim()),
            StringComparer.OrdinalIgnoreCase);
        this.patterns = patterns
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static ForbiddenWordsFilter Load(string path)
    {
        using var stream = File.OpenRead(path);
        var data = JsonSerializer.Deserialize<ForbiddenWordsData>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"Forbidden-word configuration is empty: {path}");
        if (data.Terms is null || data.Patterns is null)
        {
            throw new InvalidDataException(
                $"Forbidden-word configuration must define both 'terms' and 'patterns': {path}");
        }

        return new ForbiddenWordsFilter(data.Terms, data.Patterns);
    }

    public bool ContainsForbiddenWord(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return false;
        }

        var stripped = MentionsAndLinks.Replace(arguments, " ");
        var words = NonWordCharacters.Replace(stripped.ToLowerInvariant(), " ")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !IsWhitelisted(word))
            .ToArray();
        if (words.Length == 0)
        {
            return false;
        }

        foreach (var word in words)
        {
            if (terms.Contains(word) || terms.Contains(Transliterate(word)))
            {
                return true;
            }

            var transliterated = Transliterate(word);
            foreach (var pattern in patterns)
            {
                if (MatchesPattern(word, pattern) || MatchesPattern(transliterated, pattern))
                {
                    return true;
                }
            }
        }

        var combined = string.Concat(words);
        var normalized = NormalizeForBanCheck(combined);
        foreach (var pattern in patterns)
        {
            if (pattern.Length > 4 &&
                (combined.Contains(pattern, StringComparison.Ordinal) ||
                 normalized.Contains(NormalizeForBanCheck(pattern), StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWhitelisted(string word) =>
        WhitelistedPrefixes.Any(prefix => word.StartsWith(prefix, StringComparison.Ordinal));

    private static bool MatchesPattern(string word, string pattern)
    {
        if (pattern.Length <= 4)
        {
            return word == pattern ||
                   word.Length <= pattern.Length + 2 &&
                   word.StartsWith(pattern, StringComparison.Ordinal);
        }

        return word.Contains(pattern, StringComparison.Ordinal);
    }

    private static string NormalizeForBanCheck(string text)
    {
        var normalized = new System.Text.StringBuilder(text.Length);
        var previous = '\0';
        foreach (var character in text.ToLowerInvariant())
        {
            var mapped = character switch
            {
                'a' => 'а', 'b' => 'в', 'c' => 'с', 'd' => 'д', 'e' => 'е', 'f' => 'ф',
                'g' => 'г', 'h' => 'х', 'i' => 'и', 'j' => 'й', 'k' => 'к', 'l' => 'л',
                'm' => 'м', 'n' => 'н', 'o' => 'о', 'p' => 'р', 'q' => 'к', 'r' => 'р',
                's' => 'с', 't' => 'т', 'u' => 'у', 'v' => 'в', 'w' => 'в', 'x' => 'х',
                'y' => 'у', 'z' => 'з', '0' => 'о', '1' => 'и', '3' => 'з', '4' => 'а',
                '5' => 'с', '6' => 'б', '7' => 'т', '8' => 'в', '9' => 'д', '@' => 'а',
                '$' => 'с', _ => character
            };

            if (mapped != previous)
            {
                normalized.Append(mapped);
                previous = mapped;
            }
        }

        return NonWordCharacters.Replace(normalized.ToString(), "");
    }

    private static string Transliterate(string word)
    {
        var result = new System.Text.StringBuilder(word.Length);
        foreach (var character in word)
        {
            result.Append(character switch
            {
                'a' => 'а', 'b' => 'в', 'c' => 'к', 'd' => 'д', 'e' => 'е', 'f' => 'ф',
                'g' => 'г', 'h' => 'х', 'i' => 'и', 'j' => 'й', 'k' => 'к', 'l' => 'л',
                'm' => 'м', 'n' => 'н', 'o' => 'о', 'p' => 'п', 'q' => 'к', 'r' => 'р',
                's' => 'с', 't' => 'т', 'u' => 'у', 'v' => 'в', 'w' => 'в', 'x' => 'х',
                'y' => 'ы', 'z' => 'з', _ => character
            });
        }

        return result.ToString();
    }

    private sealed record ForbiddenWordsData(string[]? Terms, string[]? Patterns);
}
