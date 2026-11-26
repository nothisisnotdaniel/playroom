using System.Text.RegularExpressions;

namespace PlayRoom.Server;

/// <summary>GameFactory's job format: one folder per job under <c>factory/jobs/</c>, read by
/// <see cref="GameJobReader"/> from its <c>state.json</c> (written only by <c>factory/tools/state.py</c>).</summary>
internal sealed class GameJobFormat : IJobFormat
{
    /// <summary>The stages of a running job.</summary>
    private static readonly string[] Active = ["spike", "prototype", "design", "build", "playtest", "review", "approve"];

    /// <summary>The stages of a job that waits for the user.</summary>
    private static readonly string[] Blocked = ["needs-human"];

    /// <summary>The stage of a finished job.</summary>
    private const string Done = "done";

    /// <summary>Every stage, in state.py's order.</summary>
    private static readonly string[] All = [.. Active, .. Blocked, Done];

    /// <summary>The files whose changes the change fingerprint watches.</summary>
    private static readonly string[] Records = [GameJobReader.StateFileName];

    public string JobsFolderPath => "factory/jobs";

    public IReadOnlyList<string> RecordFileNames => Records;

    public IReadOnlyList<string> Stages => All;

    public IReadOnlyList<string> ActiveStages => Active;

    public IReadOnlyList<string> BlockedStages => Blocked;

    public string DoneStage => Done;

    public IReadOnlyList<string> ReviewKeys => GameJobReader.ReviewerKeys;

    public Regex IdPattern => GameJobReader.IdPattern();

    public IReadOnlyList<RoomJobFolder> JobFolders(string factoryRoot)
    {
        return [.. GameJobReader.ListFolders(factoryRoot).Select(folder => new RoomJobFolder(folder.Id, folder.Path))];
    }

    public IReadOnlyList<RoomJob> ReadAll(string factoryRoot)
    {
        return [.. GameJobReader.ReadAll(factoryRoot).Select(ToRoomJob)];
    }

    /// <summary>The fields RoomKit's views read. GameFactory has no CI status in a job.</summary>
    public static RoomJob ToRoomJob(GameJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return new RoomJob(
            job.Id,
            job.Project,
            job.Title,
            job.Stage,
            job.Mode,
            job.Path,
            job.Round,
            job.Decision,
            job.Note,
            null,
            job.Tokens,
            job.Updated);
    }
}
