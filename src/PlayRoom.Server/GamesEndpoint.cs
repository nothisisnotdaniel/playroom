namespace PlayRoom.Server;

/// <summary>A budget of <c>GAME.md</c> and how much of it the baseline uses: the worst value of the metric over
/// the baseline's policies, <c>null</c> when the baseline didn't measure it.</summary>
internal sealed record BudgetUse(string Metric, double Max, double? Used);

/// <summary>One game card of <c>GET /api/games</c>.</summary>
internal sealed record GameCard(
    string Name,
    string? Engine,
    string? Run,
    bool ShotSupported,
    AdapterCheck? Check,
    Baseline? Baseline,
    IReadOnlyList<BudgetUse> Budgets,
    IReadOnlyList<PlaytestTarget> Targets,
    int FeelChecks);

/// <summary><c>GET /api/games</c>: one card per game (a project of the factory root with a <c>backlog.md</c>, not
/// the factory): engine, run command, the last adapter check, the baseline, budgets with their use and the
/// difficulty targets, and how many feel checks wait for the user. Read-only.</summary>
internal static class GamesEndpoint
{
    public const string Route = "/api/games";

    public static void Map(WebApplication app)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], (FactoryRoot root, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(Build(root.Path));
        });
    }

    public static IReadOnlyList<GameCard> Build(string factoryRoot)
    {
        IReadOnlyList<BoardTask> tasks = BoardEndpoint.Tasks(factoryRoot);
        List<GameCard> cards = [];
        foreach (ProjectFolder game in Games(factoryRoot))
        {
            IReadOnlyDictionary<string, string> keys = GameFiles.AdapterKeys(game.Path);
            string? gameMd = GameFiles.Read(game.Path, "GAME.md");
            Baseline? baseline = GameFiles.ReadBaseline(game.Path);
            cards.Add(new GameCard(
                game.Name,
                keys.TryGetValue("engine", out string? engine) && engine.Length > 0 ? engine : null,
                GameFiles.Command(keys.GetValueOrDefault("run")),
                GameFiles.Command(keys.GetValueOrDefault("shot")) is not null,
                GameFiles.ReadCheck(factoryRoot, game.Name),
                baseline,
                Budgets(GameFiles.Budgets(gameMd), baseline),
                GameFiles.Targets(gameMd, "Difficulty targets"),
                tasks.Count(t => t.FeelCheck && string.Equals(t.Project, game.Name, StringComparison.Ordinal))));
        }

        return cards;
    }

    /// <summary>The games of the root: projects (not the factory) that have a <c>backlog.md</c>.</summary>
    public static IReadOnlyList<ProjectFolder> Games(string factoryRoot)
    {
        return [.. ProjectFinder.Find(factoryRoot).Where(p => p.Name != "factory"
            && SafeFileReader.ReadText(Path.Combine(p.Path, "backlog.md"), GameFiles.MaxTextBytes) is not null)];
    }

    /// <summary>Each budget in metric order with the worst baseline value of its metric.</summary>
    public static IReadOnlyList<BudgetUse> Budgets(IReadOnlyDictionary<string, double> budgets, Baseline? baseline)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        List<BudgetUse> uses = [];
        foreach ((string metric, double max) in budgets.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            double? used = null;
            foreach (IReadOnlyDictionary<string, double> policy in baseline?.Policies.Values ?? [])
            {
                if (policy.TryGetValue(metric, out double value))
                {
                    used = used is null ? value : Math.Max(used.Value, value);
                }
            }

            uses.Add(new BudgetUse(metric, max, used));
        }

        return uses;
    }
}
