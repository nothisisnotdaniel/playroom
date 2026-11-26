using System.Text.RegularExpressions;

namespace PlayRoom.Server;

/// <summary>One <c>[h]</c> backlog line in the <c>GET /api/board</c> response: a task for the user. A feel check
/// (title <c>Feel check: …</c>) carries what to try (its story's <c>## What</c> without the <c>Run:</c> line) and
/// the run command from that line.</summary>
internal sealed record BoardTask(
    string Project, int? Number, string Title, string Rest, bool FeelCheck, string? What, string? Run);

/// <summary>The <c>GET /api/board</c> response.</summary>
internal sealed record BoardResponse(IReadOnlyList<GameJob> Jobs, IReadOnlyList<BoardTask> Tasks);

/// <summary><c>GET /api/board</c>: every job (from <see cref="GameJobReader"/>) and the <c>[h]</c> lines of every
/// game's backlog and of the factory's own <c>factory/backlog.md</c>. Read-only, no parameters.</summary>
internal static partial class BoardEndpoint
{
    public const string Route = "/api/board";

    public const string FeelCheckPrefix = "Feel check:";

    public static void Map(WebApplication app)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], (FactoryRoot root, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(Build(root.Path));
        });
    }

    public static BoardResponse Build(string factoryRoot)
    {
        return new BoardResponse(GameJobReader.ReadAll(factoryRoot), Tasks(factoryRoot));
    }

    /// <summary>The <c>[h]</c> lines of every project in project order, then the factory's, each in file order; an
    /// unreadable backlog adds none.</summary>
    public static IReadOnlyList<BoardTask> Tasks(string factoryRoot)
    {
        List<BoardTask> tasks = [];
        List<ProjectFolder> projects = [.. ProjectFinder.Find(factoryRoot)];
        string factory = Path.Combine(factoryRoot, "factory");
        if (PlaytestView.IsPlainFolder(factory))
        {
            projects.Add(new ProjectFolder("factory", factory));
        }

        foreach (ProjectFolder project in projects)
        {
            Backlog? backlog = BacklogReader.Read(project);
            if (backlog is null)
            {
                continue;
            }

            foreach (BacklogEntry entry in backlog.User)
            {
                tasks.Add(Task(project, entry));
            }
        }

        return tasks;
    }

    /// <summary>The task of one <c>[h]</c> entry; for a feel check with a readable story, its What and Run.</summary>
    private static BoardTask Task(ProjectFolder project, BacklogEntry entry)
    {
        bool feel = entry.Title.StartsWith(FeelCheckPrefix, StringComparison.Ordinal);
        string? story = feel ? ReadStory(project, entry.Story) : null;
        (string? what, string? run) = story is null ? (null, null) : StoryParts(story);
        return new BoardTask(project.Name, entry.Number, entry.Title, entry.Rest, feel, what, run);
    }

    /// <summary>A story file of the project, only for a link of the form <c>backlog/&lt;nnn&gt;-&lt;slug&gt;.md</c>;
    /// <c>null</c> otherwise or when it can't be read.</summary>
    public static string? ReadStory(ProjectFolder project, string? link)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (link is null || !StoryLink().IsMatch(link)
            || !PlaytestView.IsPlainFolder(Path.Combine(project.Path, "backlog")))
        {
            return null;
        }

        return SafeFileReader.ReadText(Path.Combine(project.Path, "backlog", link["backlog/".Length..]), GameFiles.MaxTextBytes);
    }

    /// <summary>The What of a story without its <c>Run: `cmd`</c> line, and the command of that line.</summary>
    public static (string? What, string? Run) StoryParts(string story)
    {
        string what = string.Join('\n', GameFiles.Section(story, "What")).Trim();
        Match run = RunLine().Match(what);
        if (run.Success)
        {
            what = what.Remove(run.Index, run.Length).Trim();
        }

        return (what.Length == 0 ? null : what, run.Success ? run.Groups[1].Value : null);
    }

    [GeneratedRegex(@"\Abacklog/[0-9]{3,6}-[a-z0-9-]{1,80}\.md\z", RegexOptions.CultureInvariant)]
    private static partial Regex StoryLink();

    [GeneratedRegex(@"^Run: `([^`]+)`[^\n]*", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex RunLine();
}
