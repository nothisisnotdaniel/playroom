using System.Net;
using System.Text.Json;

namespace PlayRoom.Server.Tests;

/// <summary><c>GET /api/assets</c>: the asset ledger checked the way <c>job.py ledger-check</c> checks it, from git
/// when the game is a repo and from the disk otherwise.</summary>
public sealed class AssetsApiTests
{
    private const string Ledger =
        "# Hopper — asset ledger\n\n"
        + "| path | source | licence | author | bytes |\n"
        + "|---|---|---|---|---|\n"
        + "| assets/hero.png | code-drawn | own | factory | 5 |\n"
        + "| assets/jump.wav | tools/sfx.py jump | GPL-3.0 | someone | 3 |\n"
        + "| assets/tiles.png | code-drawn | CC0-1.0 | factory | 999 |\n"
        + "| assets/gone.png | code-drawn | own | factory | 1 |\n";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assets_ListsMissingBadLicenceWrongSizeAndUnused(bool git)
    {
        using TempFactoryRoot root = new();
        string game = Sample(root);
        if (git)
        {
            File.WriteAllText(Path.Combine(game, ".gitignore"), "assets/*.tmp\n");
            TempFactoryRoot.InitGit(game, "develop");
            // Untracked but not ignored: still an asset.
            File.WriteAllText(Path.Combine(game, "assets", "new.ogg"), "x");
            File.WriteAllText(Path.Combine(game, "assets", "cache.tmp"), "ignored");
        }

        using JsonDocument document = await Get(root, "Hopper");
        JsonElement assets = document.RootElement;

        Assert.Equal(git, assets.GetProperty("fromGit").GetBoolean());
        Assert.Equal("assets", assets.GetProperty("folder").GetString());
        Assert.Equal(["own", "CC0-1.0"], Strings(assets, "licences"));
        string[] expectedMissing = git ? ["assets/music/theme.ogg", "assets/new.ogg"] : ["assets/music/theme.ogg"];
        Assert.Equal(expectedMissing, Strings(assets, "missing"));
        Assert.Equal(["assets/jump.wav (GPL-3.0)"], Strings(assets, "badLicence"));
        Assert.Equal(["assets/tiles.png (999 in ASSETS.md, 4 on disk)"], Strings(assets, "wrongSize"));
        Assert.Equal(["assets/gone.png"], assets.GetProperty("unused").EnumerateArray()
            .Select(r => r.GetProperty("path").GetString()));
        JsonElement hero = assets.GetProperty("files").EnumerateArray()
            .Single(f => f.GetProperty("path").GetString() == "assets/hero.png");
        Assert.True(hero.GetProperty("licenceOk").GetBoolean());
        Assert.True(hero.GetProperty("sizeOk").GetBoolean());
        Assert.Equal(5, hero.GetProperty("bytes").GetInt64());
    }

    [Fact]
    public async Task Assets_WalkSkipsSymlinksAndDotFiles()
    {
        using TempFactoryRoot root = new();
        string game = Sample(root);
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.png"), "s");
        Directory.CreateSymbolicLink(Path.Combine(game, "assets", "linked"), outside);
        File.CreateSymbolicLink(Path.Combine(game, "assets", "link.png"), Path.Combine(outside, "secret.png"));
        File.WriteAllText(Path.Combine(game, "assets", ".DS_Store"), "x");

        using JsonDocument document = await Get(root, "Hopper");

        Assert.Equal(["assets/music/theme.ogg"], Strings(document.RootElement, "missing"));
    }

    [Fact]
    public async Task Assets_FolderLeavingTheGameFallsBackToAssets()
    {
        using TempFactoryRoot root = new();
        string game = Sample(root);
        File.WriteAllText(Path.Combine(game, "CLAUDE.md"), "# Hopper\n\n## Adapter\n\nassets: ../../etc\n");

        using JsonDocument document = await Get(root, "Hopper");

        Assert.Equal("assets", document.RootElement.GetProperty("folder").GetString());
    }

    [Theory]
    [InlineData("?project=Nope")]
    [InlineData("?project=factory")]
    [InlineData("?project=..")]
    [InlineData("")]
    [InlineData("?project=Hopper&project=Hopper")]
    public async Task Assets_RefusalsAreTheFixedNotFound(string query)
    {
        using TempFactoryRoot root = new();
        Sample(root);
        root.AddProject("factory", "# factory\n", "# factory backlog\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync("/api/assets" + query);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("{\"error\":\"not found\"}", await response.Content.ReadAsStringAsync());
    }

    private static string Sample(TempFactoryRoot root)
    {
        string game = GameSamples.AddGame(root);
        TempFactoryRoot.AddFile(game, "ASSETS.md", Ledger);
        TempFactoryRoot.AddFile(game, "assets/hero.png", "hero!");
        TempFactoryRoot.AddFile(game, "assets/jump.wav", "abc");
        TempFactoryRoot.AddFile(game, "assets/tiles.png", "tile");
        TempFactoryRoot.AddFile(game, "assets/music/theme.ogg", "la");
        return game;
    }

    private static async Task<JsonDocument> Get(TempFactoryRoot root, string project)
    {
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using HttpResponseMessage response = await client.GetAsync("/api/assets?project=" + project);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static string[] Strings(JsonElement value, string name)
    {
        return [.. value.GetProperty(name).EnumerateArray().Select(e => e.GetString()!)];
    }
}
