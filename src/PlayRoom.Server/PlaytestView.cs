using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlayRoom.Server;

/// <summary>One metric of one policy in a round: the baseline value, the previous round's value, this round's
/// value and the change against the baseline (<c>null</c> when there is no baseline value).</summary>
internal sealed record MetricRow(string Policy, string Metric, double? Baseline, double? Previous, double Value, double? Delta);

/// <summary>One playtest gate: its name (<c>crashes</c>, <c>softlocks</c>, <c>target metric (policy)</c> or
/// <c>budget metric</c>), whether it passed and what was measured against what.</summary>
internal sealed record PlaytestGate(string Name, bool Ok, string Detail);

/// <summary>A run of the bots that crashed or softlocked: its policy and seed, so it can be run again.</summary>
internal sealed record BadRun(string Policy, long Seed, string Outcome);

/// <summary>The playtest panel of one round of a job.</summary>
internal sealed record PlaytestRoundView(
    int Round,
    string? Result,
    int? Runs,
    int? Crashes,
    int? Softlocks,
    IReadOnlyList<MetricRow> Metrics,
    IReadOnlyList<PlaytestGate> Gates,
    IReadOnlyList<BadRun> BadRuns,
    IReadOnlyList<string> Shots);

/// <summary>Builds a job's playtest panels from its <c>state.json</c> rounds (or <c>round-&lt;n&gt;/playtest.json</c>
/// when the state has none for a round), the game's baseline, targets and budgets, the spec's playtest targets,
/// the bots output <c>round-&lt;n&gt;/bots-&lt;policy&gt;.json</c> and the PNG names in
/// <c>round-&lt;n&gt;/shots/</c>. Read-only; nothing is followed through a symlink.</summary>
internal static partial class PlaytestView
{
    public const long MaxBotsBytes = 8 * 1024 * 1024;

    /// <summary>The most shots listed per round.</summary>
    public const int MaxShots = 60;

    /// <summary>The most bad runs listed per round.</summary>
    public const int MaxBadRuns = 50;

    /// <summary>The panels of every round from 1 to the job's round (at most <see cref="GameJobReader.MaxRounds"/>)
    /// that has a result or shots.</summary>
    public static IReadOnlyList<PlaytestRoundView> Build(string factoryRoot, JobFolder folder, GameJob job)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(job);
        string? game = GameFolder(factoryRoot, job.Project);
        string? gameMd = game is null ? null : GameFiles.Read(game, "GAME.md");
        string? spec = SafeFileReader.ReadText(Path.Combine(folder.Path, "spec.md"), GameFiles.MaxTextBytes);
        Baseline? baseline = game is null ? null : GameFiles.ReadBaseline(game);
        IReadOnlyList<PlaytestTarget> targets = GameFiles.TargetsFor(gameMd, spec);
        IReadOnlyDictionary<string, double> budgets = GameFiles.Budgets(gameMd);

        List<PlaytestRoundView> views = [];
        PlaytestRound? previous = null;
        int last = Math.Min(Math.Max(job.Round ?? 0, job.Playtest.Count == 0 ? 0 : job.Playtest[^1].Round),
            GameJobReader.MaxRounds);
        for (int n = 1; n <= last; n++)
        {
            string roundPath = Path.Combine(folder.Path, "round-" + n.ToString(CultureInfo.InvariantCulture));
            bool roundOk = IsPlainFolder(roundPath);
            PlaytestRound? round = job.Playtest.FirstOrDefault(r => r.Round == n)
                ?? (roundOk ? FromFile(roundPath, n) : null);
            IReadOnlyList<string> shots = roundOk ? Shots(roundPath) : [];
            if (round is null && shots.Count == 0)
            {
                continue;
            }

            round ??= new PlaytestRound(n, null, null, null, null,
                new Dictionary<string, IReadOnlyDictionary<string, double>>());
            views.Add(new PlaytestRoundView(
                n,
                round.Result,
                round.Runs,
                round.Crashes,
                round.Softlocks,
                Metrics(round, previous, baseline),
                Gates(round, targets, budgets),
                roundOk ? BadRuns(roundPath, round) : [],
                shots));
            previous = round;
        }

        return views;
    }

    /// <summary>The folder of the game with this name (a project of the factory root other than
    /// <c>factory</c>), or <c>null</c>.</summary>
    public static string? GameFolder(string factoryRoot, string? project)
    {
        if (string.IsNullOrEmpty(project) || project == "factory")
        {
            return null;
        }

        return ProjectFinder.Find(factoryRoot)
            .FirstOrDefault(p => string.Equals(p.Name, project, StringComparison.Ordinal))?.Path;
    }

    /// <summary>Every metric of every policy in the round, in policy and then metric order (ordinal).</summary>
    public static IReadOnlyList<MetricRow> Metrics(PlaytestRound round, PlaytestRound? previous, Baseline? baseline)
    {
        ArgumentNullException.ThrowIfNull(round);
        List<MetricRow> rows = [];
        foreach ((string policy, IReadOnlyDictionary<string, double> metrics) in round.Metrics.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            foreach ((string metric, double value) in metrics.OrderBy(m => m.Key, StringComparer.Ordinal))
            {
                double? before = Value(baseline?.Policies, policy, metric);
                double? last = Value(previous?.Metrics, policy, metric);
                rows.Add(new MetricRow(policy, metric, before, last, value,
                    before is double b ? Math.Round(value - b, 6) : null));
            }
        }

        return rows;
    }

    /// <summary>The gates of the playtester: no crash, no softlock, every target in range for its policy and every
    /// budget kept by every policy. A target or budget whose metric the round did not measure is left out.</summary>
    public static IReadOnlyList<PlaytestGate> Gates(
        PlaytestRound round, IReadOnlyList<PlaytestTarget> targets, IReadOnlyDictionary<string, double> budgets)
    {
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(budgets);
        List<PlaytestGate> gates = [];
        if (round.Crashes is int crashes)
        {
            gates.Add(new PlaytestGate("crashes", crashes == 0, Format(crashes) + " crashed"));
        }

        if (round.Softlocks is int softlocks)
        {
            gates.Add(new PlaytestGate("softlocks", softlocks == 0, Format(softlocks) + " softlocked"));
        }

        foreach (PlaytestTarget target in targets)
        {
            if (Value(round.Metrics, target.Policy, target.Metric) is double value)
            {
                gates.Add(new PlaytestGate(
                    "target " + target.Metric + " (" + target.Policy + ")",
                    value >= target.Min && value <= target.Max,
                    Format(value) + " in " + Format(target.Min) + "–" + Format(target.Max)));
            }
        }

        foreach ((string metric, double max) in budgets.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            List<(string Policy, double Value)> measured = [.. round.Metrics
                .Where(p => p.Value.ContainsKey(metric))
                .Select(p => (p.Key, p.Value[metric]))
                .OrderBy(p => p.Key, StringComparer.Ordinal)];
            if (measured.Count == 0)
            {
                continue;
            }

            (string worstPolicy, double worst) = measured.MaxBy(p => p.Value);
            gates.Add(new PlaytestGate(
                "budget " + metric,
                worst <= max,
                Format(worst) + " (" + worstPolicy + ") ≤ " + Format(max)));
        }

        return gates;
    }

    /// <summary>The runs with outcome <c>crash</c> or <c>softlock</c> in the round's bots files, for each policy of
    /// the round, in policy and then file order; at most <see cref="MaxBadRuns"/>.</summary>
    public static IReadOnlyList<BadRun> BadRuns(string roundPath, PlaytestRound round)
    {
        ArgumentNullException.ThrowIfNull(round);
        List<BadRun> bad = [];
        foreach (string policy in round.Metrics.Keys.Order(StringComparer.Ordinal))
        {
            if (!GameJobReader.NamePattern().IsMatch(policy))
            {
                continue;
            }

            using JsonDocument? document = GameFiles.Json(Path.Combine(roundPath, "bots-" + policy + ".json"), MaxBotsBytes);
            if (document is null || document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("runs", out JsonElement runs) || runs.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement run in runs.EnumerateArray())
            {
                string? outcome = GameFiles.Text(run, "outcome");
                if (outcome is not ("crash" or "softlock")
                    || !run.TryGetProperty("seed", out JsonElement seed)
                    || seed.ValueKind != JsonValueKind.Number || !seed.TryGetInt64(out long number))
                {
                    continue;
                }

                bad.Add(new BadRun(policy, number, outcome));
                if (bad.Count >= MaxBadRuns)
                {
                    return bad;
                }
            }
        }

        return bad;
    }

    /// <summary>The PNG names in <c>shots/</c> of the round that match <see cref="ShotName"/> and are regular files,
    /// not symlinks, ordinal order, at most <see cref="MaxShots"/>; empty when <c>shots</c> is missing or a
    /// symlink.</summary>
    public static IReadOnlyList<string> Shots(string roundPath)
    {
        string shots = Path.Combine(roundPath, "shots");
        if (!IsPlainFolder(shots))
        {
            return [];
        }

        List<string> names = [];
        try
        {
            foreach (FileInfo file in new DirectoryInfo(shots).EnumerateFiles())
            {
                if (ShotName().IsMatch(file.Name) && file.LinkTarget is null)
                {
                    names.Add(file.Name);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        names.Sort(StringComparer.Ordinal);
        return names.Count > MaxShots ? names[..MaxShots] : names;
    }

    /// <summary>True when the path is a folder and not a symlink.</summary>
    public static bool IsPlainFolder(string path)
    {
        try
        {
            DirectoryInfo folder = new(path);
            return folder.Exists && folder.LinkTarget is null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The round from <c>round-&lt;n&gt;/playtest.json</c>, or <c>null</c>.</summary>
    private static PlaytestRound? FromFile(string roundPath, int n)
    {
        using JsonDocument? document = GameFiles.Json(Path.Combine(roundPath, "playtest.json"));
        return document is { RootElement.ValueKind: JsonValueKind.Object }
            ? GameJobReader.Round(n, document.RootElement)
            : null;
    }

    private static double? Value(IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>? policies, string policy, string metric)
    {
        return policies is not null && policies.TryGetValue(policy, out IReadOnlyDictionary<string, double>? metrics)
            && metrics.TryGetValue(metric, out double value)
            ? value
            : null;
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>A shot file name: <c>&lt;scene&gt;-&lt;moment&gt;.png</c>, a letter or digit first, then letters,
    /// digits, <c>.</c>, <c>_</c> or <c>-</c>.</summary>
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,120}\.png\z", RegexOptions.CultureInvariant)]
    public static partial Regex ShotName();
}
