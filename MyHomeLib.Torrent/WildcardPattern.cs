using System.Text.RegularExpressions;

namespace MyHomeListServer.Torrent;

public class WildcardPattern
{
    private readonly string _expression;
    private readonly Regex _regex;

    public WildcardPattern(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) throw new ArgumentNullException(nameof(pattern));
       
        _expression = "^" + Regex.Escape(pattern)
            .Replace("\\\\\\?","??").Replace("\\?", ".").Replace("??","\\?")
            .Replace("\\\\\\*","**").Replace("\\*", ".*").Replace("**","\\*") + "$";
        _regex = new Regex(_expression, RegexOptions.Compiled);
    }

    public bool IsMatch(string value)
    {
        return _regex.IsMatch(value);
    }
}