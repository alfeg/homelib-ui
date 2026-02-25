using System.Text.RegularExpressions;

namespace MyHomeLib.Torrent;

public static partial class MagnetUriHelper
{
    private static readonly Regex HashRe = MyRegex();

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

        var m = HashRe.Match(candidate);
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        throw new FormatException($"Cannot extract info-hash from magnet URI: {magnetUri}");
    }

    [GeneratedRegex(@"xt=urn:btih:([0-9a-fA-F]{40})", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-GB")]
    private static partial Regex MyRegex();
}
