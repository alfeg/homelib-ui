using System.Text.Json;

namespace MyHomeLib.Web.Services.Search;

/// <summary>
/// Maps INPX genre codes ("sf_heroic") to human labels, in Russian or English.
/// </summary>
/// <remarks>
/// The JSON files are the SPA's, linked into this project by the csproj rather than copied,
/// so the codes stay in one place. A code with no entry falls back to the code itself —
/// real collections carry genres the label files have never heard of.
/// </remarks>
public class GenreLabelService(IWebHostEnvironment environment, ILogger<GenreLabelService> logger)
{
    private readonly Dictionary<string, Dictionary<string, GenreLabel>> _byLocale = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public string Label(string code, string locale = "ru")
    {
        var labels = Load(locale);
        return labels.TryGetValue(code, out var label) && !string.IsNullOrWhiteSpace(label.Name)
            ? label.Name
            : code;
    }

    public string Category(string code, string locale = "ru")
    {
        var labels = Load(locale);
        return labels.TryGetValue(code, out var label) && !string.IsNullOrWhiteSpace(label.Category)
            ? label.Category!
            : "";
    }

    /// <summary>Genre codes grouped by their category, for a browsable two-level menu.</summary>
    public IReadOnlyList<GenreCategory> Categorise(IReadOnlyDictionary<string, int> counts, string locale = "ru")
    {
        return counts
            .Where(entry => entry.Value > 0)
            .GroupBy(entry => Category(entry.Key, locale) is { Length: > 0 } category ? category : "…")
            .Select(group => new GenreCategory(
                group.Key,
                group.Sum(entry => entry.Value),
                [.. group
                    .Select(entry => new GenreEntry(entry.Key, Label(entry.Key, locale), entry.Value))
                    .OrderByDescending(g => g.Count)]))
            .OrderByDescending(category => category.Count)
            .ToList();
    }

    private Dictionary<string, GenreLabel> Load(string locale)
    {
        locale = locale.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";

        lock (_gate)
        {
            if (_byLocale.TryGetValue(locale, out var cached)) return cached;

            // The linked JSON lands next to the assembly, which is the content root only
            // for a published app — under `dotnet run` the two directories differ.
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Resources", "Genres", $"genres.{locale}.json"),
                Path.Combine(environment.ContentRootPath, "Resources", "Genres", $"genres.{locale}.json"),
            };

            var path = candidates.FirstOrDefault(File.Exists) ?? candidates[0];
            Dictionary<string, GenreLabel> labels;

            try
            {
                using var stream = File.OpenRead(path);
                labels = JsonSerializer.Deserialize<Dictionary<string, GenreLabel>>(stream, MeiliClient.Json)
                         ?? [];
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Genres] could not read {Path}; falling back to raw codes", path);
                labels = [];
            }

            _byLocale[locale] = labels;
            return labels;
        }
    }

    public class GenreLabel
    {
        public string? Category { get; set; }
        public string? Name { get; set; }
    }
}

public record GenreEntry(string Code, string Label, int Count);

public record GenreCategory(string Name, int Count, IReadOnlyList<GenreEntry> Genres);
