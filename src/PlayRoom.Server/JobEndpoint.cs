using System.Globalization;
using Microsoft.Extensions.Primitives;

namespace PlayRoom.Server;

/// <summary>One record of a job: its file name (relative to the job folder), its tab label, whether it could be
/// read and its block tree (empty when not readable).</summary>
internal sealed record JobRecord(string Name, string Label, bool Readable, IReadOnlyList<StoryBlock> Blocks);

/// <summary>The follow-up item of a prototype's <c>verdict.md</c> (its <c>Title:</c> line) and, once the
/// orchestrator added it, its backlog number and mark (<c>?</c> while it waits for approval).</summary>
internal sealed record FollowUp(string Title, int? Number, string? Mark);

/// <summary>The <c>GET /api/job</c> response.</summary>
internal sealed record JobResponse(
    GameJob Job,
    IReadOnlyList<JobRecord> Records,
    IReadOnlyList<JobAgent> Agents,
    IReadOnlyList<PlaytestRoundView> Playtest,
    string? Verdict,
    FollowUp? FollowUp);

/// <summary><c>GET /api/job?id=&lt;id&gt;</c>: one job, its Markdown records and its playtest panels. The id is
/// matched exactly (ordinal) against the listed job folders and never joined into a path; record names come only
/// from a fixed list. Every refusal is the same fixed 404.</summary>
internal static class JobEndpoint
{
    public const string Route = "/api/job";

    public const string NotFoundBody = "{\"error\":\"not found\"}";

    public const long MaxRecordBytes = 1024 * 1024;

    private static readonly (string Name, string Label)[] LeadingRecords =
    [
        ("spec.md", "spec"),
        ("spec-review.md", "design check"),
        ("task.md", "task"),
        ("prototype.md", "prototype"),
        ("findings.md", "findings"),
        ("build.md", "build"),
        ("tuning.md", "tuning"),
    ];

    private static readonly (string Name, string Label)[] TrailingRecords =
    [
        ("verdict.md", "verdict"),
        ("feedback.md", "feedback"),
        ("decision.md", "decision"),
    ];

    private static readonly string[] Verdicts = ["KEEP", "KILL", "PIVOT"];

    public static void Map(WebApplication app)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], (FactoryRoot root, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            JobResponse? job = Build(root.Path, context.Request.Query["id"]);
            if (job is null)
            {
                return Results.Content(NotFoundBody, "application/json", statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Json(job);
        });
    }

    public static JobResponse? Build(string factoryRoot, StringValues idValue)
    {
        if (idValue.Count != 1)
        {
            return null;
        }

        JobFolder? folder = GameJobReader.Find(factoryRoot, idValue[0]);
        GameJob? job = folder is null ? null : GameJobReader.Read(folder);
        if (folder is null || job is null)
        {
            return null;
        }

        return new JobResponse(job, Records(folder, job.Round ?? 0), job.Agents ?? [],
            PlaytestView.Build(factoryRoot, folder, job), Verdict(folder), FollowUpOf(factoryRoot, folder, job.Project));
    }

    /// <summary>The verdict of the first line of <c>verdict.md</c> (<c>VERDICT: KEEP|KILL|PIVOT</c>).</summary>
    public static string? Verdict(JobFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        string? text = SafeFileReader.ReadText(Path.Combine(folder.Path, "verdict.md"), MaxRecordBytes);
        string first = (text ?? "").Split('\n')[0].Trim();
        const string Prefix = "VERDICT: ";
        if (!first.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string verdict = first[Prefix.Length..].Trim();
        return Verdicts.Contains(verdict) ? verdict : null;
    }

    /// <summary>The follow-up item named by <c>verdict.md</c>'s <c>## Follow-up item</c> <c>Title:</c> line, with the
    /// open, approval or user line of the job's project that has exactly that title; <c>null</c> without one.</summary>
    public static FollowUp? FollowUpOf(string factoryRoot, JobFolder folder, string? project)
    {
        ArgumentNullException.ThrowIfNull(folder);
        string? text = SafeFileReader.ReadText(Path.Combine(folder.Path, "verdict.md"), MaxRecordBytes);
        string? title = GameFiles.Section(text, "Follow-up item")
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("Title:", StringComparison.Ordinal))
            .Select(line => line["Title:".Length..].Trim())
            .FirstOrDefault(line => line.Length > 0);
        if (title is null)
        {
            return null;
        }

        ProjectFolder? game = ProjectFinder.Find(factoryRoot)
            .FirstOrDefault(p => string.Equals(p.Name, project, StringComparison.Ordinal));
        Backlog? backlog = game is null ? null : BacklogReader.Read(game);
        BacklogEntry? entry = backlog is null ? null : backlog.Open.Concat(backlog.Approval).Concat(backlog.User)
            .FirstOrDefault(e => e.Number is not null && string.Equals(e.Title, title, StringComparison.Ordinal));
        return new FollowUp(title, entry?.Number, entry?.Mark);
    }

    /// <summary>The records that exist, in the fixed order: leading files, then each round's playtest report and
    /// reviews, then verdict, feedback and decision.</summary>
    private static List<JobRecord> Records(JobFolder folder, int round)
    {
        List<JobRecord> records = [];
        foreach ((string name, string label) in LeadingRecords)
        {
            AddIfPresent(records, folder.Path, name, label, followable: true);
        }

        for (int n = 1; n <= Math.Min(round, GameJobReader.MaxRounds); n++)
        {
            string roundName = "round-" + n.ToString(CultureInfo.InvariantCulture);
            bool roundIsLink = IsLink(Path.Combine(folder.Path, roundName));
            AddIfPresent(records, folder.Path, roundName + "/playtest.md", "R" + n + " playtest", followable: !roundIsLink);
            foreach (string key in GameJobReader.ReviewerKeys)
            {
                AddIfPresent(records, folder.Path, roundName + "/review-" + key + ".md", "R" + n + " " + key,
                    followable: !roundIsLink);
            }
        }

        foreach ((string name, string label) in TrailingRecords)
        {
            AddIfPresent(records, folder.Path, name, label, followable: true);
        }

        return records;
    }

    /// <summary>Adds the record when something exists at its path. It is read only when <paramref
    /// name="followable"/> (its folder is no symlink), through <see cref="SafeFileReader"/>.</summary>
    private static void AddIfPresent(
        List<JobRecord> records, string jobPath, string name, string label, bool followable)
    {
        string path = Path.Combine(jobPath, name);
        if (!Exists(path))
        {
            return;
        }

        string? text = followable ? SafeFileReader.ReadText(path, MaxRecordBytes) : null;
        records.Add(text is null
            ? new JobRecord(name, label, false, [])
            : new JobRecord(name, label, true, StoryMarkdown.Parse(text)));
    }

    private static bool Exists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsLink(string path)
    {
        try
        {
            return new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
