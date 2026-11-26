using System.Net;

namespace PlayRoom.Server.Tests;

/// <summary>The web app shell: PlayRoom's own names and scripts on RoomKit's shared ones, the Games tab, the feel
/// checks section, and nothing of ControlRoom.</summary>
public sealed class WebAppTests
{
    [Fact]
    public async Task Index_IsPlayRoomWithGamesTabAndFeelChecks()
    {
        string index = await GetText("/");

        Assert.Contains("<title>PlayRoom</title>", index, StringComparison.Ordinal);
        Assert.Contains("/play.js", index, StringComparison.Ordinal);
        Assert.Contains("/play.css", index, StringComparison.Ordinal);
        Assert.DoesNotContain("act.js", index, StringComparison.Ordinal);
        Assert.Contains("id=\"games\"", index, StringComparison.Ordinal);
        Assert.Contains("id=\"assets\"", index, StringComparison.Ordinal);
        Assert.Contains("id=\"feel-checks\"", index, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlRoom", index, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/board.js")]
    [InlineData("/app.js")]
    [InlineData("/play.js")]
    [InlineData("/room.js")]
    [InlineData("/status.js")]
    [InlineData("/manifest.webmanifest")]
    public async Task OwnFiles_NameNoOtherApp(string path)
    {
        string text = await GetText(path);

        Assert.DoesNotContain("ControlRoom", text, StringComparison.Ordinal);
        Assert.DoesNotContain("controlroom", text, StringComparison.Ordinal);
        Assert.DoesNotContain(":8100", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Board_KnowsTheGameFactoryStagesAndReviewers()
    {
        string board = await GetText("/board.js");

        foreach (string stage in new[] { "spike", "prototype", "design", "build", "playtest", "review", "approve", "needs-human" })
        {
            Assert.Contains("\"" + stage + "\"", board, StringComparison.Ordinal);
        }

        foreach (string reviewer in new[] { "security", "gameplay", "performance", "code", "ux", "art" })
        {
            Assert.Contains("\"" + reviewer + "\"", board, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Play_DrawsTheGameViews()
    {
        string play = await GetText("/play.js");

        Assert.Contains("function renderPlaytest(", play, StringComparison.Ordinal);
        Assert.Contains("function renderVerdict(", play, StringComparison.Ordinal);
        Assert.Contains("function showGames(", play, StringComparison.Ordinal);
        Assert.Contains("function showAssets(", play, StringComparison.Ordinal);
        Assert.Contains("function showFeelChecks(", play, StringComparison.Ordinal);
        Assert.Contains("/api/shot", play, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_NamesPlayRoom()
    {
        Assert.Contains("\"PlayRoom\"", await GetText("/manifest.webmanifest"), StringComparison.Ordinal);
    }

    private static async Task<string> GetText(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
