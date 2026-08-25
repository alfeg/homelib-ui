namespace MyHomeLib.Web.Services.Search;

/// <summary>
/// Text folding applied to both indexed values and incoming queries.
/// </summary>
/// <remarks>
/// Russian writes ё and е interchangeably, and a collection this size contains the same
/// title spelled both ways. Meilisearch treats them as different characters — it lowercases
/// and normalises Unicode, but nothing folds U+0451 onto U+0435, and no index setting does
/// it either. The effect measured against a real collection:
///
/// <list type="bullet">
/// <item>Words of five characters or more survive on typo tolerance, which silently spends
/// the word's one-typo budget on the ё so a genuine typo elsewhere can no longer match.</item>
/// <item>Shorter words get no tolerance at all and split outright: "мёд" found 55 books,
/// "мед" found 3,856.</item>
/// <item>Quoted phrases disable typo tolerance and split the same way.</item>
/// </list>
///
/// So the folding happens here instead: searchable copies of the text are folded at index
/// time, queries are folded on the way in, and the unfolded originals stay on the document
/// for display so titles still render with their ё.
/// </remarks>
public static class RussianText
{
    /// <summary>Replaces ё with е (and Ё with Е). Returns the input when there is nothing to do.</summary>
    public static string Fold(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.IndexOf('ё') < 0 && value.IndexOf('Ё') < 0) return value;

        return string.Create(value.Length, value, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = source[i] switch
                {
                    'ё' => 'е',
                    'Ё' => 'Е',
                    var other => other,
                };
            }
        });
    }
}
