using System.Reflection;

namespace MyHomeLib.Web.Services;

/// <summary>
/// Which source this binary was built from, for the footer of the /m pages.
/// </summary>
/// <remarks>
/// The SDK appends the commit to the informational version (<c>1.0.0+&lt;sha&gt;</c>) when it
/// can see a git repository. The Docker build has no .git, so the Dockerfile passes the
/// commit in as <c>SourceRevisionId</c>; a build with neither shows no commit at all.
/// </remarks>
public static class BuildInfo
{
    public const string RepositoryUrl = "https://github.com/alfeg/homelib-ui";

    /// <summary>Full commit hash, or null when the build did not record one.</summary>
    public static string? Commit { get; } = ReadCommit();

    public static string? ShortCommit => Commit?[..Math.Min(7, Commit.Length)];

    public static string? CommitUrl => Commit is null ? null : $"{RepositoryUrl}/commit/{Commit}";

    private static string? ReadCommit()
    {
        var version = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        var plus = version?.IndexOf('+') ?? -1;
        if (plus < 0) return null;

        var commit = version![(plus + 1)..].Trim();
        return commit.Length > 0 ? commit : null;
    }
}
