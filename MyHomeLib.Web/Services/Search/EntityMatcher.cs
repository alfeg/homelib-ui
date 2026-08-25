namespace MyHomeLib.Web.Services.Search;

/// <param name="Name">Author or series name, exactly as indexed.</param>
/// <param name="Count">Books by/in it among the current results.</param>
public record EntityHit(string Name, int Count);

/// <summary>
/// Decides when a free-text query is really naming an author or a series, so the results
/// page can offer a card for the entity instead of only a flat list of its books.
/// </summary>
/// <remarks>
/// The candidates come from the facet distribution of the query's own results, so this adds
/// no extra search — the work is deciding which facet values the user actually typed.
///
/// Matching is prefix-based with a tolerance for Russian inflection: a reader searching
/// "стругацкие" should still be offered "Аркадий Натанович Стругацкий", where a strict
/// prefix test fails on the final letter. Cyrillic ё is folded to е, as in the index.
/// </remarks>
public static class EntityMatcher
{
    private const int MaxHits = 4;

    /// <summary>A value must back at least this many books before it earns a card.</summary>
    private const int MinBooks = 2;

    public static IReadOnlyList<EntityHit> Match(string? query, IReadOnlyDictionary<string, int> counts)
    {
        if (string.IsNullOrWhiteSpace(query) || counts.Count == 0) return [];

        var tokens = Tokenize(query);
        if (tokens.Count == 0) return [];

        return counts
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Where(entry => entry.Value >= MinBooks)
            .Where(entry => IsHit(tokens, entry.Key))
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key.Length)
            .Take(MaxHits)
            .Select(entry => new EntityHit(entry.Key, entry.Value))
            .ToList();
    }

    /// <summary>
    /// A hit when some query token lines up with a whole word of the name. One token is
    /// enough: "стругацкие понедельник" names both an author and a book, and the author
    /// card is exactly what the reader wants surfaced.
    /// </summary>
    private static bool IsHit(IReadOnlyList<string> tokens, string value)
    {
        var words = Tokenize(value);
        if (words.Count == 0) return false;

        return tokens.Any(token => words.Any(word => WordsAlign(token, word)));
    }

    private static bool WordsAlign(string token, string word)
    {
        // Short tokens would match far too much ("на", "и"), so they never carry a card.
        if (token.Length < 4) return false;
        if (word.StartsWith(token, StringComparison.Ordinal)) return true;

        // Inflection changes the ending, so allow one differing trailing character on each
        // side — "стругацкие" still reaches "Стругацкий". Anything looser starts matching
        // merely similar surnames: "война" would otherwise pull in the author "Войнов".
        var shared = CommonPrefixLength(token, word);
        return shared >= token.Length - 1
            && shared >= word.Length - 1;
    }

    private static int CommonPrefixLength(string a, string b)
    {
        var limit = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < limit && a[i] == b[i]) i++;
        return i;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                var lower = char.ToLowerInvariant(ch);
                current.Append(lower == 'ё' ? 'е' : lower);
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }
}
