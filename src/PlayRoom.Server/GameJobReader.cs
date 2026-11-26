using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PlayRoom.Server;

/// <summary>A job folder under <c>factory/jobs</c>: its name (the job id) and full path.</summary>
internal sealed record JobFolder(string Id, string Path);

/// <summary>One agent of a job's <c>tokens</c> map: its key, the sum of its <c>input</c>, <c>cacheRead</c> and
/// <c>output</c> (<c>null</c> when it has no valid count) and its whole <c>seconds</c> (<c>null</c> when unknown).</summary>
internal sealed record JobAgent(string Name, long? Tokens, long? Seconds);

/// <summary>One round's playtest result from <c>state.json</c>'s <c>playtest</c> map: the round number, the
/// result (<c>PASS</c> or <c>FAIL</c>), the run, crash and softlock counts and the summary metrics per bot
/// policy (only numbers are kept).</summary>
internal sealed record PlaytestRound(
    int Round,
    string? Result,
    int? Runs,
    int? Crashes,
    int? Softlocks,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Metrics);

/// <summary>A job's feel check: the backlog item it added and the run command for it.</summary>
internal sealed record FeelCheck(int? Item, string? Run);

/// <summary>One GameFactory job as the board shows it, mapped field by field from its <c>state.json</c>. A field
/// that is missing or of the wrong type is <c>null</c> (or an empty list or map); fields it doesn't know are
/// dropped. <c>Tokens</c> is the saturating sum over every agent in <c>tokens</c>. <c>Updated</c>, <c>Started</c>
/// and <c>Ended</c> are kept only when they are exactly <c>YYYY-MM-DDTHH:MM:SSZ</c>; <c>Updated</c> and
/// <c>Agents</c> are never sent in a job's JSON.</summary>
internal sealed record GameJob(
    string Id,
    string? Project,
    string? Title,
    string? Branch,
    string? Stage,
    int? Round,
    string? Mode,
    string? Path,
    string? Risk,
    IReadOnlyDictionary<string, string> Reviews,
    IReadOnlyList<IReadOnlyDictionary<string, string>> PastRounds,
    string? DesignReview,
    bool? DesignRevised,
    string? Decision,
    string? Note,
    long? Tokens,
    IReadOnlyList<string> Flags,
    IReadOnlyList<PlaytestRound> Playtest,
    FeelCheck? FeelCheck,
    [property: JsonIgnore] DateTimeOffset? Updated = null,
    string? Started = null,
    string? Ended = null,
    [property: JsonIgnore] IReadOnlyList<JobAgent>? Agents = null);

/// <summary>Reads the jobs folder <c>&lt;root&gt;/factory/jobs</c>. The folder is refused when <c>factory</c> or
/// <c>jobs</c> is a symlink; only job folders that are not symlinks and whose name matches
/// <see cref="IdPattern"/> are listed (so <c>.ids</c> and other files are skipped). Each <c>state.json</c> is read
/// through <see cref="SafeFileReader"/> (no-follow, regular file, <see cref="MaxStateBytes"/> cap) and mapped to a
/// <see cref="GameJob"/>; the raw JSON is never passed on.</summary>
internal static partial class GameJobReader
{
    public const string StateFileName = "state.json";

    public const long MaxStateBytes = 256 * 1024;

    /// <summary>The most playtest rounds read from one job.</summary>
    public const int MaxRounds = 20;

    /// <summary>The reviewer keys of state.py, in display order.</summary>
    public static readonly IReadOnlyList<string> ReviewerKeys = ["security", "gameplay", "performance", "code", "ux", "art"];

    /// <summary>The flags of state.py; any other value is dropped.</summary>
    public static readonly IReadOnlyList<string> KnownFlags = ["feel-check", "assets-changed", "perf-over-budget"];

    /// <summary>The form of <c>updated</c>, <c>started</c> and <c>ended</c>.</summary>
    public const string UpdatedFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>The jobs folder, or <c>null</c> when it is missing or <c>factory</c> or <c>jobs</c> is a
    /// symlink.</summary>
    public static string? JobsFolder(string factoryRoot)
    {
        try
        {
            DirectoryInfo factory = new(System.IO.Path.Combine(factoryRoot, "factory"));
            if (!factory.Exists || factory.LinkTarget is not null)
            {
                return null;
            }

            DirectoryInfo jobs = new(System.IO.Path.Combine(factory.FullName, "jobs"));
            if (!jobs.Exists || jobs.LinkTarget is not null)
            {
                return null;
            }

            return jobs.FullName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Every valid job folder, ordered by name (ordinal).</summary>
    public static IReadOnlyList<JobFolder> ListFolders(string factoryRoot)
    {
        List<JobFolder> folders = [];
        string? jobs = JobsFolder(factoryRoot);
        if (jobs is null)
        {
            return folders;
        }

        List<DirectoryInfo> entries;
        try
        {
            entries = [.. new DirectoryInfo(jobs).EnumerateDirectories()];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return folders;
        }

        foreach (DirectoryInfo entry in entries)
        {
            if (!IdPattern().IsMatch(entry.Name))
            {
                continue;
            }

            try
            {
                if (entry.LinkTarget is not null)
                {
                    continue;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            folders.Add(new JobFolder(entry.Name, entry.FullName));
        }

        folders.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return folders;
    }

    /// <summary>The listed folder with exactly this id (ordinal), or <c>null</c>. The id is never joined into a
    /// path.</summary>
    public static JobFolder? Find(string factoryRoot, string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return ListFolders(factoryRoot).FirstOrDefault(folder => string.Equals(folder.Id, id, StringComparison.Ordinal));
    }

    /// <summary>Every job whose <c>state.json</c> can be read and is a JSON object, ordered by folder name.</summary>
    public static IReadOnlyList<GameJob> ReadAll(string factoryRoot)
    {
        List<GameJob> jobs = [];
        foreach (JobFolder folder in ListFolders(factoryRoot))
        {
            GameJob? job = Read(folder);
            if (job is not null)
            {
                jobs.Add(job);
            }
        }

        return jobs;
    }

    /// <summary>The folder's job, or <c>null</c> when its <c>state.json</c> is missing, refused or not a JSON
    /// object.</summary>
    public static GameJob? Read(JobFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        string? text = SafeFileReader.ReadText(System.IO.Path.Combine(folder.Path, StateFileName), MaxStateBytes);
        return text is null ? null : Parse(folder.Id, text);
    }

    /// <summary>Maps <c>state.json</c> text to a job with the folder name as id, or <c>null</c> when the text is not
    /// a JSON object.</summary>
    public static GameJob? Parse(string id, string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            JsonElement state = document.RootElement;
            if (state.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new GameJob(
                id,
                Text(state, "project"),
                Text(state, "title"),
                Text(state, "branch"),
                Text(state, "stage"),
                Whole(state, "round"),
                Text(state, "mode"),
                Text(state, "path"),
                Text(state, "risk"),
                Reviews(Property(state, "reviews")),
                PastRounds(state),
                Text(state, "designReview"),
                Flag(state, "designRevised"),
                Text(state, "decision"),
                Text(state, "note"),
                Tokens(state),
                Flags(state),
                Playtest(state),
                ReadFeelCheck(state),
                Updated: Time(state, "updated"),
                Started: TimeText(state, "started"),
                Ended: TimeText(state, "ended"),
                Agents: Agents(state));
        }
    }

    private static JsonElement? Property(JsonElement state, string name)
    {
        return state.TryGetProperty(name, out JsonElement value) ? value : null;
    }

    private static string? Text(JsonElement state, string name)
    {
        JsonElement? value = Property(state, name);
        return value is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
    }

    private static bool? Flag(JsonElement state, string name)
    {
        return Property(state, name)?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    /// <summary>A whole number ≥ 0, or <c>null</c>.</summary>
    private static int? Whole(JsonElement state, string name)
    {
        JsonElement? value = Property(state, name);
        if (value is { ValueKind: JsonValueKind.Number } number && number.TryGetInt32(out int whole) && whole >= 0)
        {
            return whole;
        }

        return null;
    }

    /// <summary>The known reviewer keys whose value is a string; anything else is dropped.</summary>
    private static Dictionary<string, string> Reviews(JsonElement? value)
    {
        Dictionary<string, string> reviews = new(StringComparer.Ordinal);
        if (value is not { ValueKind: JsonValueKind.Object } map)
        {
            return reviews;
        }

        foreach (string key in ReviewerKeys)
        {
            if (map.TryGetProperty(key, out JsonElement review) && review.ValueKind == JsonValueKind.String)
            {
                reviews[key] = review.GetString()!;
            }
        }

        return reviews;
    }

    /// <summary>Each past round that is an object, as its reviews; other items are dropped.</summary>
    private static List<IReadOnlyDictionary<string, string>> PastRounds(JsonElement state)
    {
        List<IReadOnlyDictionary<string, string>> rounds = [];
        if (Property(state, "pastRounds") is not { ValueKind: JsonValueKind.Array } list)
        {
            return rounds;
        }

        foreach (JsonElement round in list.EnumerateArray())
        {
            if (round.ValueKind == JsonValueKind.Object)
            {
                rounds.Add(Reviews(round));
            }
        }

        return rounds;
    }

    /// <summary>The known flags, each once, in state.py's order.</summary>
    private static List<string> Flags(JsonElement state)
    {
        HashSet<string> found = new(StringComparer.Ordinal);
        if (Property(state, "flags") is { ValueKind: JsonValueKind.Array } list)
        {
            foreach (JsonElement flag in list.EnumerateArray())
            {
                if (flag.ValueKind == JsonValueKind.String)
                {
                    found.Add(flag.GetString()!);
                }
            }
        }

        return [.. KnownFlags.Where(found.Contains)];
    }

    /// <summary>Each round of <c>playtest</c> whose key is a round number from 1 to <see cref="MaxRounds"/> and whose
    /// value is an object, in round order.</summary>
    private static List<PlaytestRound> Playtest(JsonElement state)
    {
        List<PlaytestRound> rounds = [];
        if (Property(state, "playtest") is not { ValueKind: JsonValueKind.Object } map)
        {
            return rounds;
        }

        foreach (JsonProperty entry in map.EnumerateObject())
        {
            if (!RoundKey().IsMatch(entry.Name)
                || !int.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int round)
                || round < 1 || round > MaxRounds
                || entry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            rounds.Add(Round(round, entry.Value));
        }

        rounds.Sort((a, b) => a.Round.CompareTo(b.Round));
        return rounds;
    }

    /// <summary>One round's result from its JSON object (a <c>playtest</c> entry or a
    /// <c>round-&lt;n&gt;/playtest.json</c>).</summary>
    public static PlaytestRound Round(int round, JsonElement value)
    {
        string? result = Text(value, "result");
        return new PlaytestRound(
            round,
            result is "PASS" or "FAIL" ? result : null,
            Whole(value, "runs"),
            Whole(value, "crashes"),
            Whole(value, "softlocks"),
            Metrics(Property(value, "metrics")));
    }

    /// <summary>The numeric summary metrics per policy; policies or metrics with bad names or non-number values
    /// are dropped.</summary>
    public static Dictionary<string, IReadOnlyDictionary<string, double>> Metrics(JsonElement? value)
    {
        Dictionary<string, IReadOnlyDictionary<string, double>> policies = new(StringComparer.Ordinal);
        if (value is not { ValueKind: JsonValueKind.Object } map)
        {
            return policies;
        }

        foreach (JsonProperty policy in map.EnumerateObject())
        {
            if (!NamePattern().IsMatch(policy.Name) || policy.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            Dictionary<string, double> metrics = new(StringComparer.Ordinal);
            foreach (JsonProperty metric in policy.Value.EnumerateObject())
            {
                if (NamePattern().IsMatch(metric.Name)
                    && metric.Value.ValueKind == JsonValueKind.Number
                    && metric.Value.TryGetDouble(out double number)
                    && double.IsFinite(number))
                {
                    metrics[metric.Name] = number;
                }
            }

            policies[policy.Name] = metrics;
        }

        return policies;
    }

    private static FeelCheck? ReadFeelCheck(JsonElement state)
    {
        if (Property(state, "feelCheck") is not { ValueKind: JsonValueKind.Object } check)
        {
            return null;
        }

        return new FeelCheck(Whole(check, "item"), Text(check, "run"));
    }

    /// <summary>The field as its string when <see cref="Time"/> accepts it; <c>null</c> otherwise.</summary>
    private static string? TimeText(JsonElement state, string name)
    {
        return Time(state, name) is null ? null : Text(state, name);
    }

    /// <summary>The field as a UTC time, only when it is a string of exactly the form <c>YYYY-MM-DDTHH:MM:SSZ</c>
    /// that is a real date and time; <c>null</c> otherwise.</summary>
    private static DateTimeOffset? Time(JsonElement state, string name)
    {
        string? text = Text(state, name);
        if (text is null || !UpdatedPattern().IsMatch(text))
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(
            text,
            UpdatedFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset updated)
            ? updated
            : null;
    }

    /// <summary>The saturating sum over every agent object; <c>null</c> when no agent gives a valid count.</summary>
    private static long? Tokens(JsonElement state)
    {
        if (Property(state, "tokens") is not { ValueKind: JsonValueKind.Object } tokens)
        {
            return null;
        }

        long total = 0;
        bool known = false;
        foreach (JsonProperty agent in tokens.EnumerateObject())
        {
            if (AgentTokens(agent.Value) is long value)
            {
                known = true;
                total = total > long.MaxValue - value ? long.MaxValue : total + value;
            }
        }

        return known ? total : null;
    }

    /// <summary>One agent per object in <c>tokens</c>, in file order.</summary>
    private static List<JobAgent> Agents(JsonElement state)
    {
        List<JobAgent> agents = [];
        if (Property(state, "tokens") is not { ValueKind: JsonValueKind.Object } tokens)
        {
            return agents;
        }

        foreach (JsonProperty agent in tokens.EnumerateObject())
        {
            if (agent.Value.ValueKind == JsonValueKind.Object)
            {
                agents.Add(new JobAgent(agent.Name, AgentTokens(agent.Value), AgentSeconds(agent.Value)));
            }
        }

        return agents;
    }

    private static long? AgentTokens(JsonElement agent)
    {
        if (agent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        long total = 0;
        bool known = false;
        foreach (string name in new[] { "input", "cacheRead", "output" })
        {
            if (agent.TryGetProperty(name, out JsonElement count)
                && count.ValueKind == JsonValueKind.Number
                && count.TryGetInt64(out long value)
                && value >= 0)
            {
                known = true;
                total = total > long.MaxValue - value ? long.MaxValue : total + value;
            }
        }

        return known ? total : null;
    }

    private static long? AgentSeconds(JsonElement agent)
    {
        return agent.TryGetProperty("seconds", out JsonElement seconds)
            && seconds.ValueKind == JsonValueKind.Number
            && seconds.TryGetInt64(out long value)
            && value >= 0
                ? value
                : null;
    }

    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z\z", RegexOptions.CultureInvariant)]
    private static partial Regex UpdatedPattern();

    [GeneratedRegex(@"\A[1-9][0-9]{0,2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex RoundKey();

    /// <summary>A policy or metric name: a letter, then letters, digits, <c>-</c> or <c>_</c>, at most 40.</summary>
    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9_-]{0,39}\z", RegexOptions.CultureInvariant)]
    public static partial Regex NamePattern();

    /// <summary>A GameFactory job id: <c>&lt;nnn&gt;-&lt;slug&gt;.&lt;32 hex&gt;</c> (job.py's <c>JOB_ID</c>).</summary>
    [GeneratedRegex(@"\A[0-9]{3,6}-[a-z0-9-]{1,80}\.[0-9a-f]{32}\z", RegexOptions.CultureInvariant)]
    public static partial Regex IdPattern();
}
