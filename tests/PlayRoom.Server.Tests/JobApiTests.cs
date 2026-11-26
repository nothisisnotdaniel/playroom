using System.Net;
using System.Text.Json;

namespace PlayRoom.Server.Tests;

/// <summary><c>GET /api/job</c>: the job's fields, its records in order with their labels, the playtest panels
/// (metrics against baseline and previous round, gates, bad runs, shots) and a prototype's verdict.</summary>
public sealed class JobApiTests
{
    [Fact]
    public async Task Job_ListsRecordsInOrderWithLabels()
    {
        using JsonDocument job = await Get(GameSamples.JobId);

        string[] names = [.. job.RootElement.GetProperty("records").EnumerateArray()
            .Select(r => r.GetProperty("name").GetString()!)];
        string[] labels = [.. job.RootElement.GetProperty("records").EnumerateArray()
            .Select(r => r.GetProperty("label").GetString()!)];
        Assert.Equal(
            ["spec.md", "spec-review.md", "build.md", "round-1/playtest.md", "round-1/review-gameplay.md",
             "round-2/playtest.md", "round-2/review-gameplay.md", "round-2/review-art.md", "decision.md"],
            names);
        Assert.Equal(
            ["spec", "design check", "build", "R1 playtest", "R1 gameplay", "R2 playtest", "R2 gameplay", "R2 art",
             "decision"],
            labels);
    }

    [Fact]
    public async Task Job_MapsKnownFieldsAndDropsTheRest()
    {
        using JsonDocument document = await Get(GameSamples.JobId);
        JsonElement job = document.RootElement.GetProperty("job");

        Assert.Equal("review", job.GetProperty("stage").GetString());
        Assert.Equal(2, job.GetProperty("round").GetInt32());
        Assert.Equal(["gameplay", "art"], job.GetProperty("reviews").EnumerateObject().Select(p => p.Name).Order().Reverse());
        Assert.Equal(["feel-check", "assets-changed"], job.GetProperty("flags").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal([1, 2], job.GetProperty("playtest").EnumerateArray().Select(r => r.GetProperty("round").GetInt32()));
        Assert.Equal(4, job.GetProperty("feelCheck").GetProperty("item").GetInt32());
        Assert.False(job.TryGetProperty("updated", out _));
    }

    [Fact]
    public async Task Job_PlaytestPanelComparesWithBaselineAndPreviousRound()
    {
        using JsonDocument document = await Get(GameSamples.JobId);
        JsonElement[] rounds = [.. document.RootElement.GetProperty("playtest").EnumerateArray()];

        Assert.Equal(2, rounds.Length);
        JsonElement second = rounds[1];
        Assert.Equal("PASS", second.GetProperty("result").GetString());
        JsonElement frame = second.GetProperty("metrics").EnumerateArray()
            .Single(m => m.GetProperty("metric").GetString() == "frameMsP95");
        Assert.Equal("greedy", frame.GetProperty("policy").GetString());
        Assert.Equal(12.5, frame.GetProperty("baseline").GetDouble());
        Assert.Equal(13, frame.GetProperty("previous").GetDouble());
        Assert.Equal(18, frame.GetProperty("value").GetDouble());
        Assert.Equal(5.5, frame.GetProperty("delta").GetDouble());
        Assert.Equal(["level-1.png", "start-first-frame.png"],
            second.GetProperty("shots").EnumerateArray().Select(s => s.GetString()));
    }

    [Fact]
    public async Task Job_GatesUseSpecTargetsAndBudgets()
    {
        using JsonDocument document = await Get(GameSamples.JobId);
        JsonElement[] rounds = [.. document.RootElement.GetProperty("playtest").EnumerateArray()];

        Dictionary<string, bool> first = Gates(rounds[0]);
        Dictionary<string, bool> second = Gates(rounds[1]);
        Assert.False(first["crashes"]);
        Assert.False(first["target completionRate (greedy)"]);
        Assert.True(second["crashes"]);
        Assert.True(second["softlocks"]);
        // The spec's 0.6–0.9 replaces GAME.md's 0.5–0.95 for this job.
        Assert.Contains("0.6–0.9", Detail(rounds[1], "target completionRate (greedy)"), StringComparison.Ordinal);
        Assert.True(second["target deathsMean (random)"]);
        Assert.False(second["budget frameMsP95"]);
        Assert.False(second.ContainsKey("budget memoryMbMax"));
    }

    [Fact]
    public async Task Job_ListsCrashAndSoftlockRunsWithTheirSeeds()
    {
        using JsonDocument document = await Get(GameSamples.JobId);
        JsonElement first = document.RootElement.GetProperty("playtest")[0];

        Assert.Equal(
            ["crash greedy 7", "softlock greedy 9"],
            first.GetProperty("badRuns").EnumerateArray().Select(r =>
                r.GetProperty("outcome").GetString() + " " + r.GetProperty("policy").GetString() + " "
                + r.GetProperty("seed").GetInt64()));
    }

    [Fact]
    public async Task Job_RoundFromPlaytestJsonWhenStateHasNone()
    {
        using TempFactoryRoot root = new();
        GameSamples.AddGame(root);
        root.AddJob(GameSamples.JobId, "{\"project\":\"Hopper\",\"stage\":\"playtest\",\"round\":1}");
        root.AddRecord(GameSamples.JobId, "round-1/playtest.json",
            "{\"result\":\"FAIL\",\"runs\":10,\"crashes\":0,\"softlocks\":2,\"metrics\":{\"random\":{\"deathsMean\":3}}}");

        using JsonDocument document = await Get(root, GameSamples.JobId);
        JsonElement round = document.RootElement.GetProperty("playtest")[0];

        Assert.Equal("FAIL", round.GetProperty("result").GetString());
        Assert.Equal(2, round.GetProperty("softlocks").GetInt32());
        Assert.False(Gates(round)["softlocks"]);
    }

    [Fact]
    public async Task Job_PrototypeShowsVerdictAndFollowUpWaitingForApproval()
    {
        using TempFactoryRoot root = new();
        GameSamples.AddGame(root);
        root.AddJob(GameSamples.PrototypeId, GameSamples.PrototypeState);
        root.AddRecord(GameSamples.PrototypeId, "prototype.md", "# Grapple\n");
        root.AddRecord(GameSamples.PrototypeId, "verdict.md", GameSamples.Verdict);

        using JsonDocument document = await Get(root, GameSamples.PrototypeId);

        Assert.Equal("KEEP", document.RootElement.GetProperty("verdict").GetString());
        JsonElement followUp = document.RootElement.GetProperty("followUp");
        Assert.Equal("Grapple hook", followUp.GetProperty("title").GetString());
        Assert.Equal(3, followUp.GetProperty("number").GetInt32());
        Assert.Equal("?", followUp.GetProperty("mark").GetString());
        Assert.Equal(["prototype", "verdict"], document.RootElement.GetProperty("records").EnumerateArray()
            .Select(r => r.GetProperty("label").GetString()));
    }

    [Fact]
    public async Task Job_FollowUpNotYetInTheBacklogHasNoNumber()
    {
        using TempFactoryRoot root = new();
        GameSamples.AddGame(root);
        root.AddJob(GameSamples.PrototypeId, GameSamples.PrototypeState);
        root.AddRecord(GameSamples.PrototypeId, "verdict.md", "VERDICT: PIVOT\n\n## Follow-up item\n\nTitle: Swing rope\n");

        using JsonDocument document = await Get(root, GameSamples.PrototypeId);

        Assert.Equal("PIVOT", document.RootElement.GetProperty("verdict").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("followUp").GetProperty("number").ValueKind);
    }

    [Fact]
    public async Task Job_UnknownVerdictIsNull()
    {
        using TempFactoryRoot root = new();
        GameSamples.AddGame(root);
        root.AddJob(GameSamples.PrototypeId, GameSamples.PrototypeState);
        root.AddRecord(GameSamples.PrototypeId, "verdict.md", "VERDICT: MAYBE\n");

        using JsonDocument document = await Get(root, GameSamples.PrototypeId);

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("verdict").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("followUp").ValueKind);
    }

    [Fact]
    public async Task Job_SymlinkedRoundIsListedButNotRead()
    {
        using TempFactoryRoot root = new();
        GameSamples.AddGame(root);
        root.AddJob(GameSamples.JobId, "{\"project\":\"Hopper\",\"stage\":\"review\",\"round\":1}");
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(Path.Combine(outside, "shots"));
        File.WriteAllText(Path.Combine(outside, "playtest.md"), "PLAYTEST: PASS secret\n");
        File.WriteAllBytes(Path.Combine(outside, "shots", "a.png"), GameSamples.Png);
        Directory.CreateSymbolicLink(Path.Combine(root.JobsPath, GameSamples.JobId, "round-1"), outside);

        using JsonDocument document = await Get(root, GameSamples.JobId);

        JsonElement record = document.RootElement.GetProperty("records").EnumerateArray().Single();
        Assert.Equal("R1 playtest", record.GetProperty("label").GetString());
        Assert.False(record.GetProperty("readable").GetBoolean());
        Assert.Empty(document.RootElement.GetProperty("playtest").EnumerateArray());
        Assert.DoesNotContain("secret", document.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("?id=nope")]
    [InlineData("?id=..%2F..%2Fetc")]
    [InlineData("")]
    [InlineData("?id=" + GameSamples.JobId + "&id=" + GameSamples.JobId)]
    public async Task Job_RefusalsAreTheFixedNotFound(string query)
    {
        using TempFactoryRoot root = new();
        GameSamples.AddGame(root);
        GameSamples.AddJob(root);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync("/api/job" + query);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("{\"error\":\"not found\"}", await response.Content.ReadAsStringAsync());
    }

    internal static async Task<JsonDocument> Get(TempFactoryRoot root, string id)
    {
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using HttpResponseMessage response = await client.GetAsync("/api/job?id=" + Uri.EscapeDataString(id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonDocument> Get(string id)
    {
        using TempFactoryRoot root = new();
        GameSamples.AddGame(root);
        GameSamples.AddJob(root);
        return await Get(root, id);
    }

    private static Dictionary<string, bool> Gates(JsonElement round)
    {
        return round.GetProperty("gates").EnumerateArray()
            .ToDictionary(g => g.GetProperty("name").GetString()!, g => g.GetProperty("ok").GetBoolean());
    }

    private static string Detail(JsonElement round, string name)
    {
        return round.GetProperty("gates").EnumerateArray()
            .Single(g => g.GetProperty("name").GetString() == name).GetProperty("detail").GetString()!;
    }
}
