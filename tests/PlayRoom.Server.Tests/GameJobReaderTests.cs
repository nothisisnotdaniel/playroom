namespace PlayRoom.Server.Tests;

/// <summary><see cref="GameJobReader"/>: fields of the wrong type are null or empty, never an error, and unknown
/// values are dropped.</summary>
public sealed class GameJobReaderTests
{
    [Fact]
    public void Parse_WrongTypesGiveNullsAndEmptyLists()
    {
        GameJob? job = GameJobReader.Parse("001-x.0123456789abcdef0123456789abcdef",
            "{\"project\":5,\"title\":[],\"stage\":{},\"round\":-1,\"reviews\":[],\"pastRounds\":{},"
            + "\"flags\":\"feel-check\",\"playtest\":[],\"feelCheck\":true,\"designRevised\":\"yes\","
            + "\"updated\":\"yesterday\",\"started\":12}");

        Assert.NotNull(job);
        Assert.Null(job.Project);
        Assert.Null(job.Title);
        Assert.Null(job.Stage);
        Assert.Null(job.Round);
        Assert.Empty(job.Reviews);
        Assert.Empty(job.PastRounds);
        Assert.Empty(job.Flags);
        Assert.Empty(job.Playtest);
        Assert.Null(job.FeelCheck);
        Assert.Null(job.DesignRevised);
        Assert.Null(job.Updated);
        Assert.Null(job.Started);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public void Parse_NonObjectIsNull(string json)
    {
        Assert.Null(GameJobReader.Parse("001-x.0123456789abcdef0123456789abcdef", json));
    }

    [Fact]
    public void Parse_PlaytestDropsBadRoundsPoliciesAndMetrics()
    {
        GameJob? job = GameJobReader.Parse("001-x.0123456789abcdef0123456789abcdef",
            "{\"playtest\":{\"0\":{},\"21\":{},\"01\":{},\"3\":\"PASS\",\"2\":{\"result\":\"MAYBE\",\"runs\":-4,"
            + "\"metrics\":{\"bad name\":{\"a\":1},\"greedy\":{\"ok\":1.5,\"text\":\"1\",\"bad key\":2},\"random\":3}},"
            + "\"1\":{\"result\":\"PASS\"}}}");

        Assert.NotNull(job);
        Assert.Equal([1, 2], job.Playtest.Select(r => r.Round));
        PlaytestRound second = job.Playtest[1];
        Assert.Null(second.Result);
        Assert.Null(second.Runs);
        Assert.Equal(["greedy"], second.Metrics.Keys);
        Assert.Equal(["ok"], second.Metrics["greedy"].Keys);
    }

    [Fact]
    public void ListFolders_SkipsBadNamesSymlinksAndALinkedJobsFolder()
    {
        using TempFactoryRoot root = new();
        root.AddJob("001-a.0123456789abcdef0123456789abcdef", "{}");
        root.AddJob(".ids", null);
        root.AddJob("1-short.0123456789abcdef0123456789abcdef", "{}");
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(root.JobsPath, "002-b.0123456789abcdef0123456789abcdef"), outside);

        Assert.Equal(["001-a.0123456789abcdef0123456789abcdef"], GameJobReader.ListFolders(root.Path).Select(f => f.Id));

        using TempFactoryRoot linked = new();
        Directory.CreateDirectory(Path.Combine(linked.Path, "factory"));
        Directory.CreateSymbolicLink(Path.Combine(linked.Path, "factory", "jobs"), root.JobsPath);
        Assert.Empty(GameJobReader.ListFolders(linked.Path));
    }
}
