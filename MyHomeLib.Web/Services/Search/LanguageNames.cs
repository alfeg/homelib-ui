namespace MyHomeLib.Web.Services.Search;

/// <summary>
/// Human names for the language codes an INPX carries.
/// </summary>
/// <remarks>
/// The codes are whatever the collection put there — mostly ISO 639-1, but a real
/// collection has 59 distinct values including empty ones and oddities. Only the languages
/// that actually appear in quantity are named; anything else falls back to the raw code,
/// which is more honest than guessing at a label.
/// </remarks>
public static class LanguageNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ru"] = "Русский",
        ["en"] = "English",
        ["uk"] = "Українська",
        ["be"] = "Беларуская",
        ["bg"] = "Български",
        ["pl"] = "Polski",
        ["cs"] = "Čeština",
        ["sk"] = "Slovenčina",
        ["sr"] = "Српски",
        ["hr"] = "Hrvatski",
        ["de"] = "Deutsch",
        ["fr"] = "Français",
        ["es"] = "Español",
        ["it"] = "Italiano",
        ["pt"] = "Português",
        ["nl"] = "Nederlands",
        ["sv"] = "Svenska",
        ["no"] = "Norsk",
        ["da"] = "Dansk",
        ["fi"] = "Suomi",
        ["et"] = "Eesti",
        ["lv"] = "Latviešu",
        ["lt"] = "Lietuvių",
        ["hu"] = "Magyar",
        ["ro"] = "Română",
        ["el"] = "Ελληνικά",
        ["tr"] = "Türkçe",
        ["he"] = "עברית",
        ["ar"] = "العربية",
        ["zh"] = "中文",
        ["ja"] = "日本語",
        ["ko"] = "한국어",
        ["ka"] = "ქართული",
        ["hy"] = "Հայերեն",
        ["kk"] = "Қазақша",
        ["tt"] = "Татарча",
        ["eo"] = "Esperanto",
        ["la"] = "Latina",
    };

    /// <summary>The language's own name, or the code itself when it is not one we know.</summary>
    public static string Label(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "—";

        var trimmed = code.Trim();
        return Names.TryGetValue(trimmed, out var name) ? name : trimmed;
    }

    /// <summary>"Русский (ru)" — the name plus the raw code, when they differ.</summary>
    public static string LabelWithCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "—";

        var trimmed = code.Trim();
        var label = Label(trimmed);
        return string.Equals(label, trimmed, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{label} ({trimmed})";
    }
}
