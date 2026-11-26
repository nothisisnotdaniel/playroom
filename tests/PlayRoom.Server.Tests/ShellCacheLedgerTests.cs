using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PlayRoom.Server.Tests;

/// <summary>The shell cache ledger: <c>shell-cache.ledger</c> holds one <c>&lt;CacheVersion&gt; &lt;sha256&gt;</c>
/// line per shell state, oldest first. A shell file that changes without a new <c>CacheVersion</c> and a new ledger
/// line fails the build, so an installed app always gets a new cache name when its files change.</summary>
public sealed partial class ShellCacheLedgerTests
{
    private const string Fix =
        "a shell file changed: raise CacheVersion in room.js and append `<new version> <hash>` to shell-cache.ledger";

    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private const string HashC = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Ledger_EndsWithTheCurrentVersionAndShellHash()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        string room = await client.GetStringAsync("/room.js");
        Match version = VersionConstant().Match(room);
        Assert.True(version.Success, "room.js declares CacheVersion");
        List<(string Path, byte[] Body)> files = [];
        foreach (string path in ShellPaths(room))
        {
            using HttpResponseMessage response = await client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, path + " gave " + response.StatusCode);
            files.Add((path, await response.Content.ReadAsByteArrayAsync()));
        }

        Assert.NotEmpty(files);
        string hash = ShellHash(files);
        string? error = Check(File.ReadAllLines(LedgerPath()), version.Groups[1].Value, hash);
        Assert.True(error is null, error + " (current shell hash: " + hash + ")");
    }

    [Fact]
    public void Check_PassesForACorrectLedger()
    {
        Assert.Null(Check(["v1 " + HashA], "v1", HashA));
        Assert.Null(Check(["v1 " + HashA, "v2 " + HashB, "v10 " + HashC], "v10", HashC));
    }

    [Fact]
    public void Check_FailsWhenTheLastHashIsNotTheCurrentShell()
    {
        string? error = Check(["v1 " + HashA, "v2 " + HashB], "v2", HashC);

        Assert.NotNull(error);
        Assert.Contains(Fix, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_FailsWhenAVersionAppearsTwice()
    {
        string? error = Check(["v1 " + HashA, "v2 " + HashB, "v2 " + HashC], "v2", HashC);

        Assert.NotNull(error);
        Assert.Contains("v2 appears twice", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("v3", "v2")]
    [InlineData("v10", "v9")]
    public void Check_FailsWhenAVersionIsNotAboveTheOneBefore(string first, string second)
    {
        string? error = Check([first + " " + HashA, second + " " + HashB], second, HashB);

        Assert.NotNull(error);
        Assert.Contains("is not above", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_FailsWhenTheLastVersionIsNotCacheVersion()
    {
        // The shell is unchanged but CacheVersion moved without a ledger line, or the line was not appended.
        string? error = Check(["v1 " + HashA, "v2 " + HashB], "v3", HashB);

        Assert.NotNull(error);
        Assert.Contains(Fix, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1")]
    [InlineData("1 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("v1 AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("v1 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("v1  aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("v1 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ")]
    public void Check_FailsForALineOfAnotherShape(string line)
    {
        string? error = Check([line, "v2 " + HashB], "v2", HashB);

        Assert.NotNull(error);
        Assert.Contains("line 1", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_FailsForAnEmptyLedger()
    {
        Assert.NotNull(Check([], "v1", HashA));
    }

    [Fact]
    public void ShellHash_ChangesWhenOneByteFlips()
    {
        byte[] script = Encoding.UTF8.GetBytes("const JobIdPattern = /^[0-9]{1,6}$/;\n");
        byte[] flipped = (byte[])script.Clone();
        flipped[7] ^= 0x01;
        (string, byte[])[] before = [("/", Encoding.UTF8.GetBytes("<!doctype html>\n")), ("/board.js", script)];
        (string, byte[])[] after = [("/", Encoding.UTF8.GetBytes("<!doctype html>\n")), ("/board.js", flipped)];

        string hash = ShellHash(before);
        Assert.Matches(HashPattern(), hash);
        Assert.Equal(hash, ShellHash(before));
        Assert.NotEqual(hash, ShellHash(after));
        // So a changed shell file without a new ledger line fails the check.
        Assert.Null(Check(["v1 " + hash], "v1", hash));
        Assert.NotNull(Check(["v1 " + hash], "v1", ShellHash(after)));
    }

    [Fact]
    public void ShellHash_CoversThePathsAndTheirOrder()
    {
        byte[] body = Encoding.UTF8.GetBytes("x");
        string hash = ShellHash([("/a.js", body), ("/b.js", body)]);

        Assert.NotEqual(hash, ShellHash([("/b.js", body), ("/a.js", body)]));
        Assert.NotEqual(hash, ShellHash([("/a.js", body), ("/c.js", body)]));
        // Path and body are framed by newlines: "/a.js\nx\n".
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("/a.js\nx\n/b.js\nx\n"))), hash);
    }

    /// <summary>The ledger check: null when the ledger is well formed, its versions strictly increase without
    /// repeats and its last line is <paramref name="cacheVersion"/> with <paramref name="currentHash"/>; else the
    /// error, which names the fix.</summary>
    internal static string? Check(IReadOnlyList<string> ledgerLines, string cacheVersion, string currentHash)
    {
        if (ledgerLines.Count == 0)
        {
            return "shell-cache.ledger is empty; " + Fix;
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        long previous = -1;
        for (int i = 0; i < ledgerLines.Count; i++)
        {
            Match line = LedgerLine().Match(ledgerLines[i]);
            if (!line.Success)
            {
                return "shell-cache.ledger line " + (i + 1) + " is not `v<number> <sha256 hex>`";
            }

            string version = "v" + line.Groups[1].Value;
            if (!seen.Add(version))
            {
                return "shell-cache.ledger: " + version + " appears twice";
            }

            if (!long.TryParse(line.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long number)
                || number <= previous)
            {
                return "shell-cache.ledger line " + (i + 1) + ": " + version + " is not above the version before it";
            }

            previous = number;
        }

        string[] last = ledgerLines[^1].Split(' ');
        if (!string.Equals(last[0], cacheVersion, StringComparison.Ordinal))
        {
            return "the last ledger version is " + last[0] + ", but CacheVersion is " + cacheVersion + "; " + Fix;
        }

        if (!string.Equals(last[1], currentHash, StringComparison.Ordinal))
        {
            return Fix;
        }

        return null;
    }

    /// <summary>SHA-256 over each shell path in list order: the UTF-8 path, a newline, the served body and a
    /// newline.</summary>
    internal static string ShellHash(IEnumerable<(string Path, byte[] Body)> files)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] newline = [(byte)'\n'];
        foreach ((string path, byte[] body) in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path));
            hash.AppendData(newline);
            hash.AppendData(body);
            hash.AppendData(newline);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string[] ShellPaths(string room)
    {
        Match list = ShellList().Match(room);
        Assert.True(list.Success, "room.js declares Shell");
        return [.. QuotedString().Matches(list.Groups[1].Value).Select(match => match.Groups[1].Value)];
    }

    /// <summary>The ledger next to this file: found by walking up from the test output to <c>PlayRoom.slnx</c>.</summary>
    private static string LedgerPath()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "PlayRoom.slnx")))
        {
            folder = folder.Parent;
        }

        Assert.NotNull(folder);
        return Path.Combine(folder.FullName, "tests", "PlayRoom.Server.Tests", "shell-cache.ledger");
    }

    [GeneratedRegex("^v(\\d+) ([0-9a-f]{64})$")]
    private static partial Regex LedgerLine();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex HashPattern();

    [GeneratedRegex("const CacheVersion = \"(v\\d+)\";")]
    private static partial Regex VersionConstant();

    [GeneratedRegex("const Shell = \\[([^\\]]*)\\];")]
    private static partial Regex ShellList();

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex QuotedString();
}
