using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PlayRoom.Server.Tests;

/// <summary>Hosts PlayRoom in memory (no real port) with <c>FactoryRoot</c> set to a temporary root and the token
/// file in its own temp folder (never the real one), deleted on dispose. Clients send
/// <c>Host: 127.0.0.1:8110</c>. The running version is <c>dev</c>, which never checks for updates.</summary>
internal sealed class ServerFactory : WebApplicationFactory<Program>
{
    /// <summary>RoomKit's setting for the token file and the file's name (RoomKit's <c>AccessToken</c> is
    /// internal).</summary>
    private const string TokenFileKey = "TokenFile";

    private const string TokenFileName = "access-token";

    private readonly string factoryRoot;

    private readonly string tokenFolder;

    public ServerFactory(string factoryRoot)
    {
        this.factoryRoot = factoryRoot;
        // The in-memory host's default Host is "localhost" with no port, which the Host check refuses.
        ClientOptions.BaseAddress = new Uri("http://127.0.0.1:8110/");
        tokenFolder = Path.Combine(Path.GetTempPath(), "playroom-token-" + Guid.NewGuid().ToString("N"));
        TokenFile = Path.Combine(tokenFolder, "token", TokenFileName);
    }

    /// <summary>The token file the hosted server uses.</summary>
    public string TokenFile { get; }

    /// <summary>The stored token: the one ASCII line the server writes on start.</summary>
    public string ReadToken()
    {
        string token = File.ReadAllText(TokenFile).Trim();
        Assert.NotEmpty(token);
        return token;
    }

    /// <summary>A client that sends <c>Authorization: Bearer &lt;token&gt;</c> on every request.</summary>
    public HttpClient CreateTokenClient()
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ReadToken());
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("FactoryRoot", factoryRoot);
        builder.UseSetting(TokenFileKey, TokenFile);
        builder.UseSetting(FactoryBacklog.ConfigKey, factoryRoot.TrimEnd('/', '\\') + "-factory-develop");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(tokenFolder))
        {
            Directory.Delete(tokenFolder, recursive: true);
        }
    }
}
