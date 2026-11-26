using System.Net;
using System.Text.Json;

namespace PlayRoom.Server.Tests;

/// <summary><c>GET /api/board</c>: every job and the tasks for the user, with feel checks carrying what to try and
/// the command to run the game.</summary>
public sealed class BoardApiTests
{
    [Fact]
    public async Task Board_FeelCheckTaskHasWhatAndRun()
    {
        using TempFactoryRoot root = new();
        GameSamples.AddGame(root);
        GameSamples.AddJob(root);

        using JsonDocument document = await Get(root);
        JsonElement[] tasks = [.. document.RootElement.GetProperty("tasks").EnumerateArray()];

        Assert.Equal(2, tasks.Length);
        JsonElement feel = tasks[0];
        Assert.True(feel.GetProperty("feelCheck").GetBoolean());
        Assert.Equal(4, feel.GetProperty("number").GetInt32());
        Assert.Equal("python3 tools/run.py --seed 1", feel.GetProperty("run").GetString());
        Assert.Equal("Jump ten times and say whether the arc feels floaty.", feel.GetProperty("what").GetString());
        JsonElement other = tasks[1];
        Assert.False(other.GetProperty("feelCheck").GetBoolean());
        Assert.Equal(JsonValueKind.Null, other.GetProperty("run").ValueKind);
    }

    [Fact]
    public async Task Board_ListsJobsWithFlagsAndPlaytest()
    {
        using TempFactoryRoot root = new();
        GameSamples.AddGame(root);
        GameSamples.AddJob(root);
        root.AddJob("not-a-job", "{}");
        root.AddJob("003-broken.00000000000000000000000000000000", "[1,2]");

        using JsonDocument document = await Get(root);
        JsonElement job = document.RootElement.GetProperty("jobs").EnumerateArray().Single();

        Assert.Equal(GameSamples.JobId, job.GetProperty("id").GetString());
        Assert.Equal(2, job.GetProperty("flags").GetArrayLength());
        Assert.Equal(2, job.GetProperty("playtest").GetArrayLength());
    }

    [Fact]
    public async Task Board_IncludesTheFactorysOwnTasks()
    {
        using TempFactoryRoot root = new();
        GameSamples.AddGame(root);
        TempFactoryRoot.AddFile(root.Path, "factory/backlog.md", "# factory backlog\n\n- [h] Pick two engines: for stage 7\n");

        using JsonDocument document = await Get(root);

        Assert.Contains(document.RootElement.GetProperty("tasks").EnumerateArray(),
            t => t.GetProperty("project").GetString() == "factory" && !t.GetProperty("feelCheck").GetBoolean());
    }

    [Fact]
    public async Task Board_FeelCheckStoryOutsideBacklogFolderIsNotRead()
    {
        using TempFactoryRoot root = new();
        string game = GameSamples.AddGame(root);
        File.WriteAllText(Path.Combine(game, "backlog.md"),
            "# Hopper backlog\n\n- [h] [#4](../secret.md) Feel check: jump arc\n");
        File.WriteAllText(Path.Combine(root.Path, "secret.md"), "## What\n\nsecret\n\nRun: `rm -rf /`\n");

        using JsonDocument document = await Get(root);

        Assert.DoesNotContain("secret", document.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("rm -rf", document.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    private static async Task<JsonDocument> Get(TempFactoryRoot root)
    {
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using HttpResponseMessage response = await client.GetAsync("/api/board");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
