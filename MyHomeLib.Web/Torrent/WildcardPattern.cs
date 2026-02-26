using System.Text.RegularExpressions;

namespace MyHomeLib.Web;

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

    public bool IsMatch(string input) => _regex.IsMatch(input);
}
