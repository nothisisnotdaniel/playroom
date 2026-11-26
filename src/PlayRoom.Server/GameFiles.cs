using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlayRoom.Server;

/// <summary>A difficulty target of <c>GAME.md</c> (or a spec's <c>## Playtest targets</c>): the metric must be
/// from <c>Min</c> to <c>Max</c> for the policy.</summary>
internal sealed record PlaytestTarget(string Metric, string Policy, double Min, double Max);

/// <summary>The last adapter check of a game (<c>factory/checks/&lt;G&gt;.json</c>): whether it passed, on which
/// commit and branch, when, and the commands that failed.</summary>
internal sealed record AdapterCheck(bool Ok, string? Sha, string? Branch, string? At, IReadOnlyList<string> Failed);

/// <summary>The game's <c>playtest/baseline.json</c>: commit, time, seed, runs per policy and the summary
/// metrics per policy.</summary>
internal sealed record Baseline(
    string? Sha, string? At, long? Seed, int? Runs,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Policies);

/// <summary>One line of a game's <c>ASSETS.md</c> ledger.</summary>
internal sealed record LedgerRow(string Path, string Source, string Licence, string Author, string Bytes);

/// <summary>Reads the GameFactory files of a game and of the factory, read-only, the way
/// <c>factory/tools/job.py</c> and <c>factory/server/serve.py</c> read them (see <c>docs/adapter.md</c>). Every
/// file goes through <see cref="SafeFileReader"/>: no-follow, regular file, a size cap.</summary>
internal static partial class GameFiles
{
    public const long MaxTextBytes = 1024 * 1024;

    /// <summary>The lines under one <c>## </c> heading of a Markdown text (case-insensitive).</summary>
    public static IReadOnlyList<string> Section(string? text, string heading)
    {
        List<string> lines = [];
        bool inside = false;
        foreach (string line in (text ?? "").Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                if (inside)
                {
                    break;
                }

                inside = string.Equals(trimmed[3..].Trim(), heading, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inside)
            {
                lines.Add(trimmed);
            }
        }

        return lines;
    }

    /// <summary>A text file of the folder, or <c>null</c> when it is missing, refused or too big.</summary>
    public static string? Read(string folder, string name)
    {
        return SafeFileReader.ReadText(Path.Combine(folder, name), MaxTextBytes);
    }

    /// <summary>The <c>key: value</c> lines of the <c>## Adapter</c> section of the game's <c>CLAUDE.md</c>.</summary>
    public static IReadOnlyDictionary<string, string> AdapterKeys(string gameFolder)
    {
        Dictionary<string, string> keys = new(StringComparer.Ordinal);
        foreach (string line in Section(Read(gameFolder, "CLAUDE.md"), "Adapter"))
        {
            Match match = AdapterKey().Match(line);
            if (match.Success)
            {
                keys[match.Groups[1].Value] = match.Groups[2].Value.Trim();
            }
        }

        return keys;
    }

    /// <summary>A command value without its backticks; <c>null</c> when it is empty or <c>unsupported</c>.</summary>
    public static string? Command(string? value)
    {
        string text = (value ?? "").Trim();
        if (text.Length == 0 || text.StartsWith("unsupported", StringComparison.Ordinal))
        {
            return null;
        }

        Match match = Backticked().Match(text);
        return match.Success ? match.Groups[1].Value : text;
    }

    /// <summary>The targets of a <c>## Difficulty targets</c> (or <c>## Playtest targets</c>) section.</summary>
    public static IReadOnlyList<PlaytestTarget> Targets(string? text, string heading)
    {
        List<PlaytestTarget> targets = [];
        foreach (string line in Section(text, heading))
        {
            Match match = TargetLine().Match(line.Trim());
            if (match.Success
                && Number(match.Groups[3].Value) is double min
                && Number(match.Groups[4].Value) is double max)
            {
                targets.Add(new PlaytestTarget(match.Groups[1].Value, match.Groups[2].Value.Trim(), min, max));
            }
        }

        return targets;
    }

    /// <summary>The game's targets with a spec's <c>## Playtest targets</c> replacing the range of each metric and
    /// policy it names (and adding the ones it adds).</summary>
    public static IReadOnlyList<PlaytestTarget> TargetsFor(string? gameMd, string? spec)
    {
        List<PlaytestTarget> targets = [.. Targets(gameMd, "Difficulty targets")];
        foreach (PlaytestTarget target in Targets(spec, "Playtest targets"))
        {
            int at = targets.FindIndex(t => t.Metric == target.Metric && t.Policy == target.Policy);
            if (at >= 0)
            {
                targets[at] = target;
            }
            else
            {
                targets.Add(target);
            }
        }

        return targets;
    }

    /// <summary>The budgets of <c>## Budgets</c>: the most each metric may be, for every policy.</summary>
    public static IReadOnlyDictionary<string, double> Budgets(string? gameMd)
    {
        Dictionary<string, double> budgets = new(StringComparer.Ordinal);
        foreach (string line in Section(gameMd, "Budgets"))
        {
            Match match = BudgetLine().Match(line.Trim());
            if (match.Success && Number(match.Groups[2].Value) is double max)
            {
                budgets[match.Groups[1].Value] = max;
            }
        }

        return budgets;
    }

    /// <summary>The licences under <c>## Allowed asset licences</c>.</summary>
    public static IReadOnlyList<string> Licences(string? gameMd)
    {
        List<string> licences = [];
        foreach (string line in Section(gameMd, "Allowed asset licences"))
        {
            Match match = LicenceLine().Match(line.Trim());
            if (match.Success)
            {
                licences.Add(match.Groups[1].Value);
            }
        }

        return licences;
    }

    /// <summary>The ledger rows of <c>ASSETS.md</c>, as job.py reads them: table lines with at least five cells,
    /// not the separator and not the <c>path</c> header; a later line for the same path wins.</summary>
    public static IReadOnlyList<LedgerRow> Ledger(string? assetsMd)
    {
        Dictionary<string, LedgerRow> rows = new(StringComparer.Ordinal);
        foreach (string raw in (assetsMd ?? "").Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith('|'))
            {
                continue;
            }

            string[] cells = [.. line.Trim('|').Split('|').Select(cell => cell.Trim().Trim('`'))];
            if (cells.Length < 5 || cells[0].All(c => c is '-' or ':' or ' ')
                || string.Equals(cells[0], "path", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rows[cells[0]] = new LedgerRow(cells[0], cells[1], cells[2], cells[3], cells[4]);
        }

        return [.. rows.Values];
    }

    /// <summary>The game's <c>playtest/baseline.json</c>, or <c>null</c> when missing or not an object.</summary>
    public static Baseline? ReadBaseline(string gameFolder)
    {
        using JsonDocument? document = Json(Path.Combine(gameFolder, "playtest", "baseline.json"));
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        JsonElement root = document.RootElement;
        return new Baseline(
            Text(root, "sha"),
            Text(root, "at"),
            root.TryGetProperty("seed", out JsonElement seed) && seed.TryGetInt64Safe(out long s) ? s : null,
            root.TryGetProperty("runs", out JsonElement runs) && runs.TryGetInt64Safe(out long r) && r is >= 0 and <= int.MaxValue
                ? (int)r : null,
            GameJobReader.Metrics(root.TryGetProperty("policies", out JsonElement policies) ? policies : null));
    }

    /// <summary>The last adapter check of the game, or <c>null</c> when there is none or it is broken.</summary>
    public static AdapterCheck? ReadCheck(string factoryRoot, string game)
    {
        using JsonDocument? document = Json(Path.Combine(factoryRoot, "factory", "checks", game + ".json"));
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        JsonElement root = document.RootElement;
        List<string> failed = [];
        if (root.TryGetProperty("results", out JsonElement results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement result in results.EnumerateArray())
            {
                if (result.ValueKind == JsonValueKind.Object
                    && !(result.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.True)
                    && Text(result, "command") is string command)
                {
                    failed.Add(command);
                }
            }
        }

        bool passed = root.TryGetProperty("ok", out JsonElement value) && value.ValueKind == JsonValueKind.True;
        return new AdapterCheck(passed, Text(root, "sha"), Text(root, "branch"), Text(root, "at"), failed);
    }

    /// <summary>A JSON file parsed, or <c>null</c> when it is missing, refused or broken.</summary>
    public static JsonDocument? Json(string path, long maxBytes = MaxTextBytes)
    {
        string? text = SafeFileReader.ReadText(path, maxBytes);
        if (text is null)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? Text(JsonElement value, string name)
    {
        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty(name, out JsonElement text) && text.ValueKind == JsonValueKind.String
            ? text.GetString()
            : null;
    }

    private static bool TryGetInt64Safe(this JsonElement value, out long number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out number);
    }

    private static double? Number(string text)
    {
        return double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }

    [GeneratedRegex(@"^\s*(?:- )?([a-z-]+):\s*(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex AdapterKey();

    [GeneratedRegex(@"\A`([^`]+)`", RegexOptions.CultureInvariant)]
    private static partial Regex Backticked();

    [GeneratedRegex(@"\A- `?([A-Za-z][A-Za-z0-9]*)`?\s*\(([^)]+)\):\s*([0-9.]+)\s*[–-]\s*([0-9.]+)", RegexOptions.CultureInvariant)]
    private static partial Regex TargetLine();

    [GeneratedRegex(@"\A- `?([A-Za-z][A-Za-z0-9]*)`?:\s*([0-9.]+)", RegexOptions.CultureInvariant)]
    private static partial Regex BudgetLine();

    [GeneratedRegex(@"\A- `?([A-Za-z0-9.+-]+)`?", RegexOptions.CultureInvariant)]
    private static partial Regex LicenceLine();
}
