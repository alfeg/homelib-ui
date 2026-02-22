using System.Text.RegularExpressions;

namespace MyHomeListServer.Torrent;

public class WildcardPattern(string pattern)
{
    private readonly Regex _regex = new(BuildExpression(pattern), RegexOptions.Compiled);

    private static string BuildExpression(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) throw new ArgumentNullException(nameof(pattern));
        
        return "^" + Regex.Escape(pattern)
            .Replace("\\\\\\?","??").Replace("\\?", ".").Replace("??","\\?")
            .Replace("\\\\\\*","**").Replace("\\*", ".*").Replace("**","\\*") + "$";
    }

    public bool IsMatch(string value)
    {
        return _regex.IsMatch(value);
    }
}