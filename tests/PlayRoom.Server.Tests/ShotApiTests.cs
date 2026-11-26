using System.Net;

namespace PlayRoom.Server.Tests;

/// <summary><c>GET /api/shot</c>: serves a listed PNG of a job's round; every other request is the fixed 404.</summary>
public sealed class ShotApiTests
{
    [Fact]
    public async Task Shot_ServesTheListedPng()
    {
        using TempFactoryRoot root = Sample();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync(
            "/api/shot?id=" + GameSamples.JobId + "&round=2&name=level-1.png");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(GameSamples.Png, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Shot_NeedsTheToken()
    {
        using TempFactoryRoot root = Sample();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            "/api/shot?id=" + GameSamples.JobId + "&round=2&name=level-1.png");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("round=2&name=level-1.png")]
    [InlineData("id=nope&round=2&name=level-1.png")]
    [InlineData("id=JOB&round=1&name=level-1.png")]
    [InlineData("id=JOB&round=02&name=level-1.png")]
    [InlineData("id=JOB&round=0&name=level-1.png")]
    [InlineData("id=JOB&round=21&name=level-1.png")]
    [InlineData("id=JOB&round=-1&name=level-1.png")]
    [InlineData("id=JOB&round=2&name=..%2F..%2Fstate.json")]
    [InlineData("id=JOB&round=2&name=notes.txt")]
    [InlineData("id=JOB&round=2&name=fake.png")]
    [InlineData("id=JOB&round=2&name=linked.png")]
    [InlineData("id=JOB&round=2&name=level-1.png&name=level-1.png")]
    public async Task Shot_RefusalsAreTheFixedNotFound(string query)
    {
        using TempFactoryRoot root = Sample();
        string shots = Path.Combine(root.JobsPath, GameSamples.JobId, "round-2", "shots");
        File.WriteAllText(Path.Combine(shots, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(shots, "fake.png"), "not a png");
        string outside = Path.Combine(root.Path, "outside.png");
        File.WriteAllBytes(outside, GameSamples.Png);
        File.CreateSymbolicLink(Path.Combine(shots, "linked.png"), outside);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync(
            "/api/shot?" + query.Replace("JOB", GameSamples.JobId, StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("{\"error\":\"not found\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Shot_SymlinkedShotsFolderIsNotListed()
    {
        using TempFactoryRoot root = new();
        GameSamples.AddGame(root);
        root.AddJob(GameSamples.JobId, "{\"project\":\"Hopper\",\"round\":1,"
            + "\"playtest\":{\"1\":{\"result\":\"PASS\"}}}");
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "a.png"), GameSamples.Png);
        Directory.CreateDirectory(Path.Combine(root.JobsPath, GameSamples.JobId, "round-1"));
        Directory.CreateSymbolicLink(Path.Combine(root.JobsPath, GameSamples.JobId, "round-1", "shots"), outside);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync(
            "/api/shot?id=" + GameSamples.JobId + "&round=1&name=a.png");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static TempFactoryRoot Sample()
    {
        TempFactoryRoot root = new();
        GameSamples.AddGame(root);
        GameSamples.AddJob(root);
        return root;
    }
}
