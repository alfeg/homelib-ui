using MyHomeLib.Web.Services.Search;

namespace MyHomeLib.Web.Models.Thin;

/// <param name="Href">Target of the row.</param>
/// <param name="Title">Primary line.</param>
/// <param name="Meta">Optional grey second line.</param>
/// <param name="Trailing">Optional right-hand marker, usually the drill-down chevron.</param>
public record RowItem(string Href, string Title, string? Meta = null, string? Trailing = null);

public record Crumb(string Href, string Text);

/// <summary>
/// Everything the shared layout needs: the header bar, the document title and whether the
/// page should reload itself.
/// </summary>
public abstract class ThinPageModel
{
    public required string Title { get; init; }
    public required string Heading { get; init; }
    public string? Subtitle { get; init; }
    public IReadOnlyList<Crumb> Crumbs { get; init; } = [];

    /// <summary>Meta-refresh interval; 0 disables it. The thin pages ship no JavaScript.</summary>
    public int AutoRefreshSeconds { get; init; }
}

public class IndexModel : ThinPageModel
{
    public required long TotalBooks { get; init; }
    public required IReadOnlyList<RowItem> Sections { get; init; }
}

public class ResultsModel : ThinPageModel
{
    public required BookSearchResult Result { get; init; }
    public required IReadOnlyList<RowItem> Books { get; init; }

    /// <summary>Null when the page carries no search box (the browse views).</summary>
    public string? SearchAction { get; init; }
    public string? Query { get; init; }
    public IReadOnlyList<(string Name, string Value)> HiddenFields { get; init; } = [];

    public IReadOnlyList<RowItem> AuthorCards { get; init; } = [];
    public IReadOnlyList<RowItem> SeriesCards { get; init; } = [];

    public string? PreviousHref { get; init; }
    public string? NextHref { get; init; }
    public string PagerCaption { get; init; } = "";

    public bool HasCards => AuthorCards.Count > 0 || SeriesCards.Count > 0;
}

public class RowsModel : ThinPageModel
{
    public required IReadOnlyList<RowItem> Rows { get; init; }
    public string? Caption { get; init; }
    public string? PreviousHref { get; init; }
    public string? NextHref { get; init; }
    public string PagerCaption { get; init; } = "";
}

public class AuthorModel : ThinPageModel
{
    public required IReadOnlyList<RowItem> Series { get; init; }
    public required IReadOnlyList<RowItem> Books { get; init; }
    public string? PreviousHref { get; init; }
    public string? NextHref { get; init; }
    public string PagerCaption { get; init; } = "";
}

public class BookModel : ThinPageModel
{
    public required BookDocument Book { get; init; }
    public IReadOnlyList<RowItem> SeriesRows { get; init; } = [];
    public IReadOnlyList<RowItem> AuthorRows { get; init; } = [];
    public IReadOnlyList<RowItem> GenreRows { get; init; } = [];
    public required IReadOnlyList<(string Label, string Value)> Facts { get; init; }
}

public class NotReadyModel : ThinPageModel
{
    public required IndexStatusSnapshot Status { get; init; }
    public required int RefreshSeconds { get; init; }
    public bool Failed => Status.Error is not null;
}
