using System.Text.RegularExpressions;

namespace MyHomeListServer.Torrent;

public static class MagnetUriHelper
{
    private static readonly Regex _hashRe =
        new(@"xt=urn:btih:([0-9a-fA-F]{40})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Extracts the 40-char hex info-hash from a magnet URI.</summary>
    public static string ParseInfoHash(string magnetUri)
    {
        if (string.IsNullOrWhiteSpace(magnetUri))
            throw new FormatException("Magnet URI is empty.");

        string candidate;
        try
        {
            candidate = Uri.UnescapeDataString(magnetUri);
        }
        catch
        {
            candidate = magnetUri;
        }

        var m = _hashRe.Match(candidate);
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        throw new FormatException($"Cannot extract info-hash from magnet URI: {magnetUri}");
    }
}
