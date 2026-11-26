namespace PlayRoom.Server.Tests;

/// <summary><see cref="GameFiles"/> reads the GameFactory files as <c>job.py</c> and <c>serve.py</c> do.</summary>
public sealed class GameFilesTests
{
    [Theory]
    [InlineData("`python3 run.py`", "python3 run.py")]
    [InlineData("`python3 run.py` (opens a window)", "python3 run.py")]
    [InlineData("python3 run.py", "python3 run.py")]
    [InlineData("", null)]
    [InlineData("unsupported (headless only)", null)]
    [InlineData(null, null)]
    public void Command_StripsBackticksAndDropsUnsupported(string? value, string? expected)
    {
        Assert.Equal(expected, GameFiles.Command(value));
    }

    [Fact]
    public void TargetsFor_SpecReplacesAndAddsTargets()
    {
        IReadOnlyList<PlaytestTarget> targets = GameFiles.TargetsFor(
            GameSamples.GameMd,
            "## Playtest targets\n\n- completionRate (greedy): 0.6–0.9\n- progressMean (random): 0.2-0.5\n");

        Assert.Equal(
            ["completionRate greedy 0.6 0.9", "deathsMean random 0 20", "progressMean random 0.2 0.5"],
            targets.Select(t => FormattableString.Invariant($"{t.Metric} {t.Policy} {t.Min} {t.Max}")));
    }

    [Fact]
    public void Budgets_AndLicencesComeFromTheirSections()
    {
        Assert.Equal(16.6, GameFiles.Budgets(GameSamples.GameMd)["frameMsP95"]);
        Assert.Equal(["own", "CC0-1.0"], GameFiles.Licences(GameSamples.GameMd));
    }

    [Fact]
    public void Ledger_SkipsHeaderAndSeparatorAndLaterRowWins()
    {
        IReadOnlyList<LedgerRow> rows = GameFiles.Ledger(
            "| path | source | licence | author | bytes |\n|---|---|---|---|---|\n"
            + "| assets/a.png | code-drawn | own | me | 3 |\n"
            + "| assets/short | x |\n"
            + "| assets/a.png | code-drawn | CC0-1.0 | me | 4 |\n");

        LedgerRow row = Assert.Single(rows);
        Assert.Equal("CC0-1.0", row.Licence);
        Assert.Equal("4", row.Bytes);
    }
}
