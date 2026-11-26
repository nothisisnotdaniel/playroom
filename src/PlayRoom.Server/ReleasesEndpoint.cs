using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace PlayRoom.Server;

/// <summary>A project's last release: the highest-SemVer <c>vX.Y.Z</c> tag and its creator date as UTC
/// <c>YYYY-MM-DDTHH:MM:SSZ</c> (<c>null</c> when git gave none).</summary>
internal sealed record ReleaseTag(string Tag, string? Date);

/// <summary>One first-parent commit on <c>develop</c> since the release: 7 hex characters, the cleaned subject and
/// the merged job's id (only for an exact <c>factory: merge &lt;id&gt;</c> subject, else <c>null</c>).</summary>
internal sealed record ReleaseCommit(string Sha, string Subject, string? Job);

/// <summary>One project in the <c>GET /api/releases</c> response. <c>Bump</c> is <c>minor</c>, <c>patch</c> or
/// <c>null</c>; <c>Suggested</c> the version without <c>v</c>, or <c>null</c>.</summary>
internal sealed record ProjectRelease(
    string Name,
    bool Available,
    ReleaseTag? Release,
    int? Commits,
    IReadOnlyList<ReleaseCommit> Log,
    string? Suggested,
    string? Bump);

/// <summary>The <c>GET /api/releases</c> response.</summary>
internal sealed record ReleasesResponse(IReadOnlyList<ProjectRelease> Projects);

/// <summary><c>GET|HEAD /api/releases</c>: per discovered project (in <see cref="ProjectFinder"/> order) the last
/// local release tag, what is on <c>develop</c> since then and the version <c>/factory release</c> would suggest.
/// Read-only, <c>no-store</c>; any query parameter gives <c>400 {"error":"invalid"}</c>. Runs only the three
/// read-only git calls of <see cref="ProjectGit.Tags"/>, <see cref="ProjectGit.CommitsSince"/> and
/// <see cref="ProjectGit.LogSince"/> per project (plus the <see cref="BacklogSource"/> reads when a merged job's
/// category is looked up), never <c>gh</c>, a fetch or the network. Request input never reaches git.</summary>
internal static partial class ReleasesEndpoint
{
    public const string Route = "/api/releases";

    public const string InvalidBody = "{\"error\":\"invalid\"}";

    /// <summary>The most log rows sent per project.</summary>
    public const int MaxRows = 50;

    /// <summary>The most characters of a subject that are sent.</summary>
    public const int MaxSubject = 200;

    public const int ShaLength = 7;

    public const string Minor = "minor";

    public const string Patch = "patch";

    private const string MergePrefix = "factory: merge ";

    public static void Map(WebApplication app)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], async (
            FactoryRoot root, BacklogWorktree worktree, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Query.Count > 0)
            {
                return Results.Content(InvalidBody, "application/json", statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Json(await Build(root.Path, worktree));
        });
    }

    public static async Task<ReleasesResponse> Build(string factoryRoot, BacklogWorktree worktree)
    {
        IReadOnlyList<ProjectFolder> projects = ProjectFinder.Find(factoryRoot);
        IReadOnlyList<GameJob>? jobs = null;
        List<ProjectRelease> releases = [];
        foreach (ProjectFolder project in projects)
        {
            releases.Add(await ForProject(project, () => jobs ??= GameJobReader.ReadAll(factoryRoot), worktree));
        }

        return new ReleasesResponse(releases);
    }

    private static async Task<ProjectRelease> ForProject(
        ProjectFolder project, Func<IReadOnlyList<GameJob>> jobs, BacklogWorktree worktree)
    {
        IReadOnlyList<(string Name, long? Unix)>? tags = await ProjectGit.Tags(project.Path);
        if (tags is null)
        {
            return Unavailable(project.Name);
        }

        ReleaseTag? release = HighestRelease(tags);
        int? commits = await ProjectGit.CommitsSince(project.Path, release?.Tag);
        if (commits is null)
        {
            return Unavailable(project.Name);
        }

        IReadOnlyList<(string Sha, string Subject)>? log = await ProjectGit.LogSince(project.Path, release?.Tag);
        if (log is null)
        {
            return Unavailable(project.Name);
        }

        List<ReleaseCommit> rows = [];
        foreach ((string sha, string subject) in log)
        {
            if (rows.Count >= MaxRows)
            {
                break;
            }

            if (ShortSha(sha) is string shortSha)
            {
                rows.Add(new ReleaseCommit(shortSha, CleanSubject(subject), MergedJob(subject)));
            }
        }

        List<string> merged = log.Select(row => MergedJob(row.Subject)).OfType<string>().ToList();
        string? bump = null;
        if (merged.Count > 0)
        {
            bump = Patch;
            ProjectFolder? source = null;
            foreach (string id in merged)
            {
                source ??= await BacklogSource.For(project, worktree);
                string? category = CategoryOf(id, source, jobs());
                if (category is null || category == "feature")
                {
                    bump = Minor;
                    break;
                }
            }
        }

        string? suggested = bump is null ? null : Suggest(release?.Tag, bump);
        return new ProjectRelease(project.Name, true, release, commits, rows, suggested, bump);
    }

    private static ProjectRelease Unavailable(string name)
    {
        return new ProjectRelease(name, false, null, null, [], null, null);
    }

    /// <summary>The highest-SemVer release tag (<see cref="ProjectGit.IsReleaseTag"/>) with its date, or
    /// <c>null</c> when there is none.</summary>
    public static ReleaseTag? HighestRelease(IReadOnlyList<(string Name, long? Unix)> tags)
    {
        (string Name, long? Unix)? best = null;
        (BigInteger, BigInteger, BigInteger) bestVersion = default;
        foreach ((string name, long? unix) in tags)
        {
            if (!ProjectGit.IsReleaseTag(name))
            {
                continue;
            }

            (BigInteger, BigInteger, BigInteger) version = Parse(name);
            if (best is null || Compare(version, bestVersion) > 0)
            {
                best = (name, unix);
                bestVersion = version;
            }
        }

        if (best is null)
        {
            return null;
        }

        return new ReleaseTag(best.Value.Name, Date(best.Value.Unix));
    }

    /// <summary>The next version for the bump from the tag's version (<c>0.0.0</c> without a tag): minor gives
    /// <c>X.(Y+1).0</c>, patch <c>X.Y.(Z+1)</c>.</summary>
    public static string Suggest(string? tag, string bump)
    {
        (BigInteger major, BigInteger minor, BigInteger patch) = tag is not null && ProjectGit.IsReleaseTag(tag)
            ? Parse(tag)
            : (BigInteger.Zero, BigInteger.Zero, BigInteger.Zero);
        return bump == Minor
            ? Format(major, minor + 1, BigInteger.Zero)
            : Format(major, minor, patch + 1);
    }

    /// <summary>The job id of an exact <c>factory: merge &lt;id&gt;</c> subject whose id matches
    /// <see cref="GameJobReader.IdPattern"/> and is not <c>backlog</c>, else <c>null</c>.</summary>
    public static string? MergedJob(string subject)
    {
        if (!subject.StartsWith(MergePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string id = subject[MergePrefix.Length..];
        return id != BacklogWorktree.BranchName && GameJobReader.IdPattern().IsMatch(id) ? id : null;
    }

    /// <summary>The subject without control characters (also the bidi controls U+200E, U+200F, U+202A–U+202E and
    /// U+2066–U+2069, so a subject can't reorder how its row reads), cut to <see cref="MaxSubject"/> characters (never
    /// inside a surrogate pair).</summary>
    public static string CleanSubject(string subject)
    {
        StringBuilder text = new();
        foreach (char c in subject)
        {
            if (!char.IsControl(c) && !IsBidiControl(c))
            {
                text.Append(c);
            }
        }

        if (text.Length <= MaxSubject)
        {
            return text.ToString();
        }

        int length = char.IsHighSurrogate(text[MaxSubject - 1]) ? MaxSubject - 1 : MaxSubject;
        return text.ToString(0, length);
    }

    private static bool IsBidiControl(char c)
    {
        return c is '\u200E' or '\u200F' or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069');
    }

    /// <summary>The first 7 characters of an abbreviated sha of at least 7 lowercase hex characters, else
    /// <c>null</c>.</summary>
    public static string? ShortSha(string sha)
    {
        return ShaPattern().IsMatch(sha) ? sha[..ShaLength] : null;
    }

    /// <summary>The category of a merged job: its <c>state.json</c> title <c>#N …</c>, then backlog item N of the
    /// project (read from <paramref name="source"/>), then that story's <c>Category:</c> line; <c>null</c> when any
    /// step is missing.</summary>
    private static string? CategoryOf(string id, ProjectFolder source, IReadOnlyList<GameJob> jobs)
    {
        GameJob? job = jobs.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (job?.Title is null)
        {
            return null;
        }

        Match match = TitleNumber().Match(job.Title);
        if (!match.Success
            || !int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
        {
            return null;
        }

        BacklogEntry? entry = BacklogReader.Read(source)?.All.FirstOrDefault(item => item.Number == number);
        return entry?.Category;
    }

    private static string? Date(long? unix)
    {
        if (unix is null || unix < 0 || unix > 253402300799)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(unix.Value).UtcDateTime
            .ToString(GameJobReader.UpdatedFormat, CultureInfo.InvariantCulture);
    }

    private static (BigInteger, BigInteger, BigInteger) Parse(string tag)
    {
        string[] parts = tag[1..].Split('.');
        return (
            BigInteger.Parse(parts[0], CultureInfo.InvariantCulture),
            BigInteger.Parse(parts[1], CultureInfo.InvariantCulture),
            BigInteger.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    private static int Compare((BigInteger, BigInteger, BigInteger) a, (BigInteger, BigInteger, BigInteger) b)
    {
        int major = a.Item1.CompareTo(b.Item1);
        if (major != 0)
        {
            return major;
        }

        int minor = a.Item2.CompareTo(b.Item2);
        return minor != 0 ? minor : a.Item3.CompareTo(b.Item3);
    }

    private static string Format(BigInteger major, BigInteger minor, BigInteger patch)
    {
        return major.ToString(CultureInfo.InvariantCulture) + "." + minor.ToString(CultureInfo.InvariantCulture)
            + "." + patch.ToString(CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"\A[0-9a-f]{7,40}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ShaPattern();

    [GeneratedRegex(@"\A#(?<n>[0-9]{1,9})(?:\s|\z)", RegexOptions.CultureInvariant)]
    private static partial Regex TitleNumber();
}
