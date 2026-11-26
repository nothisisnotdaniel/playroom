using System.Net;
using System.Text.Json;

namespace PlayRoom.Server.Tests;

/// <summary><c>GET /api/games</c>: one card per game with engine, run command, adapter check, baseline, budgets
/// with their use, difficulty targets and waiting feel checks.</summary>
public sealed class GamesApiTests
{
    [Fact]
    public async Task Games_CardHasTheGameHealth()
    {
        using TempFactoryRoot root = new();
        GameSamples.AddGame(root);

        JsonElement card = (await Get(root)).Single();

        Assert.Equal("Hopper", card.GetProperty("name").GetString());
        Assert.Equal("TestEngine 1.0", card.GetProperty("engine").GetString());
        Assert.Equal("python3 tools/run.py", card.GetProperty("run").GetString());
        Assert.True(card.GetProperty("shotSupported").GetBoolean());
        JsonElement check = card.GetProperty("check");
        Assert.False(check.GetProperty("ok").GetBoolean());
        Assert.Equal(["shot"], check.GetProperty("failed").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(20, card.GetProperty("baseline").GetProperty("runs").GetInt32());
        Assert.Equal(1, card.GetProperty("feelChecks").GetInt32());
        Assert.Equal(2, card.GetProperty("targets").GetArrayLength());
    }

    [Fact]
    public async Task Games_BudgetUseIsTheWorstBaselinePolicy()
    {
        using TempFactoryRoot root = new();
        GameSamples.AddGame(root);

        JsonElement card = (await Get(root)).Single();
        Dictionary<string, JsonElement> budgets = card.GetProperty("budgets").EnumerateArray()
            .ToDictionary(b => b.GetProperty("metric").GetString()!);

        Assert.Equal(14, budgets["frameMsP95"].GetProperty("used").GetDouble());
        Assert.Equal(16.6, budgets["frameMsP95"].GetProperty("max").GetDouble());
        Assert.Equal(310, budgets["memoryMbMax"].GetProperty("used").GetDouble());
    }

    [Fact]
    public async Task Games_NewGameWithoutChecksOrBaselineHasNulls()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Fresh", "# Fresh\n\n## Adapter\n\nengine:\nrun: unsupported (no window)\n", "# Fresh backlog\n");

        JsonElement card = (await Get(root)).Single();

        Assert.Equal(JsonValueKind.Null, card.GetProperty("engine").ValueKind);
        Assert.Equal(JsonValueKind.Null, card.GetProperty("run").ValueKind);
        Assert.False(card.GetProperty("shotSupported").GetBoolean());
        Assert.Equal(JsonValueKind.Null, card.GetProperty("check").ValueKind);
        Assert.Equal(JsonValueKind.Null, card.GetProperty("baseline").ValueKind);
        Assert.Equal(0, card.GetProperty("budgets").GetArrayLength());
    }

    [Fact]
    public async Task Games_LeavesOutTheFactoryAndFoldersWithoutBacklog()
    {
        using TempFactoryRoot root = new();
        GameSamples.AddGame(root);
        root.AddProject("factory", "# factory\n", "# factory backlog\n");
        root.AddProject("NoBacklog", "# x\n", null);

        Assert.Equal(["Hopper"], (await Get(root)).Select(c => c.GetProperty("name").GetString()));
    }

    private static async Task<JsonElement[]> Get(TempFactoryRoot root)
    {
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using HttpResponseMessage response = await client.GetAsync("/api/games");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. document.RootElement.EnumerateArray().Select(e => e.Clone())];
    }
}
