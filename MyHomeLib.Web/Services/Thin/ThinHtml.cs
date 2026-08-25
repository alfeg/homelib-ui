using System.Net;
using System.Text;

namespace MyHomeLib.Web.Services.Thin;

/// <summary>
/// Builds the minimal HTML served to e-ink readers, styled after the PocketBook firmware UI.
/// </summary>
/// <remarks>
/// Deliberately not Razor. The target is a PocketBook-class browser: an old WebKit with a
/// few megabytes of budget, a slow CPU and a screen that repaints in whole pages. So:
/// no JavaScript, no external requests, no web fonts, no flex or grid, black on white,
/// and a page that stays in the low kilobytes. Navigation is links and GET forms only.
///
/// The visual language copies the device's own apps: a double-ruled header, hairline row
/// separators, a title line over a grey meta line, uppercase section captions, square
/// bordered buttons, and inverted-on-press feedback instead of hover (e-ink has no hover,
/// and cannot render shadows or rounded corners cleanly).
/// </remarks>
public sealed class ThinHtml(string title)
{
    /// <summary>
    /// Inlined because a second request costs more than the bytes do, and because a
    /// stylesheet that fails to load on a flaky reader leaves an unusable page.
    /// Greys are limited to one value: e-ink dithers intermediate tones into noise.
    /// </summary>
    private const string Css = """
        *{box-sizing:border-box}
        body{font:16px/1.4 "PT Sans",Tahoma,Arial,sans-serif;margin:0;padding:0;color:#000;background:#fff}
        a{color:#000;text-decoration:none}
        a:active{background:#000;color:#fff}
        .hd{padding:11px 12px 9px;border-bottom:3px double #000}
        .hd h1{font-size:19px;margin:0;font-weight:bold}
        .hd .c{font-size:13px;color:#666;margin-top:3px}
        .hd .c a{text-decoration:underline}
        .bd{padding:10px 12px 20px}
        ul{list-style:none;margin:0;padding:0}
        li{border-bottom:1px solid #c8c8c8}
        li a{display:block;padding:10px 0;min-height:34px}
        .ti{font-size:17px}
        .m{display:block;color:#666;font-size:13px;margin-top:3px}
        .cap{text-transform:uppercase;letter-spacing:.08em;font-size:12px;color:#666;margin:16px 0 4px}
        form{margin:0 0 6px}
        input[type=text]{width:100%;font-size:16px;font-family:inherit;padding:10px;border:1px solid #000}
        button{font-size:16px;font-family:inherit;padding:10px 22px;border:1px solid #000;background:#fff}
        .pg{text-align:center;padding:16px 0 4px;font-size:14px;color:#666}
        .pg a{display:inline-block;min-width:66px;padding:10px 14px;border:1px solid #000;margin:0 6px;color:#000}
        .ch{float:right;color:#666}
        .d{display:block;text-align:center;padding:13px;border:2px solid #000;margin:0 0 10px;font-size:17px;font-weight:bold}
        .bar{border:1px solid #000;height:22px;margin:10px 0 6px}
        .bar i{display:block;height:20px;background:#000}
        .big{font-size:28px;font-weight:bold;margin:4px 0}
        .f{width:100%;border-collapse:collapse}
        .f th{text-align:left;font-weight:normal;color:#666;font-size:14px;padding:7px 10px 7px 0;width:45%;vertical-align:top}
        .f td{padding:7px 0;font-size:15px;vertical-align:top}
        .f tr{border-bottom:1px solid #c8c8c8}
        """;

    private readonly StringBuilder _header = new(256);
    private readonly StringBuilder _body = new(4096);
    private int _refreshSeconds;

    /// <summary>
    /// Reloads the page on a timer using a meta refresh — the only way to animate anything
    /// here, since the thin pages ship no JavaScript. Ancient reader browsers honour it.
    /// </summary>
    public ThinHtml AutoRefresh(int seconds)
    {
        _refreshSeconds = seconds;
        return this;
    }

    /// <summary>Determinate progress bar drawn with a plain block element, no images.</summary>
    public ThinHtml ProgressBar(int percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        return Raw($"<div class=\"bar\"><i style=\"width:{clamped}%\"></i></div>");
    }

    public ThinHtml Big(string text) =>
        Raw($"<div class=\"big\">{E(text)}</div>");

    /// <summary>
    /// Page header: title, an optional grey sub-line, and optional breadcrumb links.
    /// Mirrors the fixed title bar every PocketBook screen carries.
    /// </summary>
    public ThinHtml Header(string heading, string? subtitle = null, params (string Href, string Text)[] crumbs)
    {
        _header.Append("<div class=\"hd\"><h1>").Append(E(heading)).Append("</h1>");

        var parts = new List<string>();
        if (crumbs.Length > 0) parts.AddRange(crumbs.Select(c => Link(c.Href, c.Text)));
        if (!string.IsNullOrWhiteSpace(subtitle)) parts.Add(E(subtitle));

        if (parts.Count > 0)
            _header.Append("<div class=\"c\">").Append(string.Join(" · ", parts)).Append("</div>");

        _header.Append("</div>");
        return this;
    }

    public ThinHtml Raw(string html)
    {
        _body.Append(html);
        return this;
    }

    /// <summary>Uppercase section label, as used above grouped lists on the device.</summary>
    public ThinHtml Caption(string text) =>
        Raw($"<div class=\"cap\">{E(text)}</div>");

    public ThinHtml Note(string text) =>
        Raw($"<p class=\"m\">{E(text)}</p>");

    public ThinHtml SearchForm(string action, string? query, IEnumerable<(string Name, string Value)>? hidden = null)
    {
        var hiddenFields = hidden is null
            ? ""
            : string.Concat(hidden.Select(h => $"<input type=\"hidden\" name=\"{E(h.Name)}\" value=\"{E(h.Value)}\">"));

        return Raw($"""
            <form action="{E(action)}" method="get">{hiddenFields}
            <input type="text" name="q" value="{E(query ?? "")}" placeholder="Author, title or series">
            <button type="submit">Search</button></form>
            """);
    }

    public ThinHtml List(IEnumerable<string> items)
    {
        _body.Append("<ul>");
        foreach (var item in items) _body.Append("<li>").Append(item).Append("</li>");
        _body.Append("</ul>");
        return this;
    }

    /// <summary>
    /// One list row: a title line with an optional grey meta line beneath, and an optional
    /// right-hand value (a count, or the "›" chevron that marks a drill-down).
    /// </summary>
    public static string Row(string href, string title, string? meta = null, string? trailing = null)
    {
        var body = new StringBuilder();
        if (trailing is not null) body.Append("<span class=\"ch\">").Append(E(trailing)).Append("</span>");
        body.Append("<span class=\"ti\">").Append(E(title)).Append("</span>");
        if (!string.IsNullOrWhiteSpace(meta)) body.Append("<span class=\"m\">").Append(E(meta)).Append("</span>");

        return $"<a href=\"{E(href)}\">{body}</a>";
    }

    /// <summary>
    /// Label/value rows. A table rather than a list because these are genuinely tabular and
    /// the target browsers render tables reliably, where flex and grid are unavailable.
    /// </summary>
    public ThinHtml Facts(IEnumerable<(string Label, string Value)> facts)
    {
        _body.Append("<table class=\"f\">");
        foreach (var (label, value) in facts)
            _body.Append("<tr><th>").Append(E(label)).Append("</th><td>").Append(E(value)).Append("</td></tr>");
        _body.Append("</table>");
        return this;
    }

    public ThinHtml Pager(string? previousHref, string? nextHref, string caption)
    {
        _body.Append("<div class=\"pg\">");
        if (previousHref is not null) _body.Append($"<a href=\"{E(previousHref)}\">‹</a>");
        _body.Append(E(caption));
        if (nextHref is not null) _body.Append($"<a href=\"{E(nextHref)}\">›</a>");
        _body.Append("</div>");
        return this;
    }

    public static string Link(string href, string text, string? cssClass = null) =>
        $"<a href=\"{E(href)}\"{(cssClass is null ? "" : $" class=\"{cssClass}\"")}>{E(text)}</a>";

    /// <summary>HTML-escapes text. Every caller-supplied string goes through this.</summary>
    public static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    public string Render() => $"""
        <!DOCTYPE html>
        <html lang="ru"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        {(_refreshSeconds > 0 ? $"<meta http-equiv=\"refresh\" content=\"{_refreshSeconds}\">" : "")}
        <title>{E(title)}</title>
        <style>{Css}</style>
        </head><body>{_header}<div class="bd">{_body}</div></body></html>
        """;
}
