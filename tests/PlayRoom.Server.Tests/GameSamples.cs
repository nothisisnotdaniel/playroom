using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace PlayRoom.Server.Tests;

/// <summary>A sample game ("Hopper") and sample GameFactory jobs: the files <c>/game init</c> writes, a baseline,
/// an adapter check and job folders with playtest rounds and screenshots.</summary>
internal static class GameSamples
{
    public const string Game = "Hopper";

    public const string JobId = "001-jump-arc.0123456789abcdef0123456789abcdef";

    public const string PrototypeId = "002-grapple.fedcba9876543210fedcba9876543210";

    public const string ClaudeMd =
        "# Hopper\n\n"
        + "## Adapter\n\n"
        + "engine: TestEngine 1.0\n"
        + "language: Lua\n"
        + "build: `python3 tools/build.py`\n"
        + "shot: `python3 tools/shot.py`\n"
        + "run: `python3 tools/run.py`\n"
        + "assets: assets\n\n"
        + "## Determinism rules\n\nSeeded.\n";

    public const string GameMd =
        "# Hopper\n\n"
        + "## Difficulty targets\n\n"
        + "- completionRate (greedy): 0.5–0.95\n"
        + "- deathsMean (random): 0–20\n\n"
        + "## Budgets\n\n"
        + "- frameMsP95: 16.6\n"
        + "- memoryMbMax: 1024\n\n"
        + "## Allowed asset licences\n\n"
        + "- own\n"
        + "- CC0-1.0\n";

    public const string Backlog =
        "# Hopper backlog\n\n"
        + "- [x] [#1](backlog/001-set-up-the-adapter.md) Set up the adapter\n"
        + "- [ ] [#2](backlog/002-jump-arc.md) Jump arc\n"
        + "- [?] [#3](backlog/003-grapple-hook.md) Grapple hook\n"
        + "- [h] [#4](backlog/004-feel-check-jump-arc.md) Feel check: jump arc\n"
        + "- [h] [#5](backlog/005-pick-a-name.md) Pick a name\n";

    public const string FeelStory =
        "# Feel check: jump arc\n\n"
        + "## What\n\n"
        + "Jump ten times and say whether the arc feels floaty.\n\n"
        + "Run: `python3 tools/run.py --seed 1`\n";

    public const string Baseline =
        "{\"sha\":\"abc1234\",\"at\":\"2026-11-20T10:00:00Z\",\"seed\":1,\"runs\":20,"
        + "\"policies\":{\"greedy\":{\"completionRate\":0.8,\"frameMsP95\":12.5,\"memoryMbMax\":300},"
        + "\"random\":{\"deathsMean\":5,\"frameMsP95\":14,\"memoryMbMax\":310}}}";

    public const string Check =
        "{\"ok\":false,\"sha\":\"abc1234\",\"branch\":\"develop\",\"at\":\"2026-11-21T09:00:00Z\","
        + "\"results\":[{\"command\":\"build\",\"ok\":true},{\"command\":\"shot\",\"ok\":false}]}";

    /// <summary>A built job in review on round 2: round 1 failed the playtest, round 2 passed.</summary>
    public const string JobState =
        "{\"id\":\"" + JobId + "\",\"project\":\"Hopper\",\"title\":\"Jump arc\",\"branch\":\"job/001-jump-arc\","
        + "\"stage\":\"review\",\"round\":2,\"mode\":\"normal\",\"path\":\"feature\",\"risk\":\"low\","
        + "\"reviews\":{\"gameplay\":\"PASS\",\"art\":\"CHANGES\",\"bogus\":\"PASS\"},"
        + "\"pastRounds\":[{\"gameplay\":\"CHANGES\"}],"
        + "\"designReview\":\"PASS\",\"designRevised\":true,"
        + "\"flags\":[\"feel-check\",\"made-up\",\"assets-changed\",\"feel-check\"],"
        + "\"playtest\":{"
        + "\"1\":{\"result\":\"FAIL\",\"runs\":40,\"crashes\":1,\"softlocks\":0,"
        + "\"metrics\":{\"greedy\":{\"completionRate\":0.4,\"frameMsP95\":13},\"random\":{\"deathsMean\":8}}},"
        + "\"2\":{\"result\":\"PASS\",\"runs\":40,\"crashes\":0,\"softlocks\":0,"
        + "\"metrics\":{\"greedy\":{\"completionRate\":0.7,\"frameMsP95\":18},\"random\":{\"deathsMean\":6}}},"
        + "\"x\":{\"result\":\"PASS\"}},"
        + "\"feelCheck\":{\"item\":4,\"run\":\"python3 tools/run.py --seed 1\"},"
        + "\"updated\":\"2026-11-22T10:00:00Z\",\"started\":\"2026-11-22T08:00:00Z\"}";

    public const string BotsRound1 =
        "{\"runs\":[{\"seed\":1,\"outcome\":\"complete\"},{\"seed\":7,\"outcome\":\"crash\"},"
        + "{\"seed\":9,\"outcome\":\"softlock\"},{\"seed\":\"x\",\"outcome\":\"crash\"}]}";

    /// <summary>A prototype whose verdict is KEEP with the follow-up item #3 (waiting for approval).</summary>
    public const string PrototypeState =
        "{\"project\":\"Hopper\",\"title\":\"Grapple\",\"stage\":\"needs-human\",\"round\":1,\"path\":\"prototype\"}";

    public const string Verdict =
        "VERDICT: KEEP\n\nThe hook is fun.\n\n## Follow-up item\n\nTitle: Grapple hook\n";

    /// <summary>A 1×1 opaque RGB PNG.</summary>
    public static byte[] Png { get; } = MakePng();

    /// <summary>Adds Hopper with its files, a baseline, an adapter check and the feel check story, and returns its
    /// folder.</summary>
    public static string AddGame(TempFactoryRoot root)
    {
        string game = root.AddProject(Game, ClaudeMd, Backlog);
        TempFactoryRoot.AddFile(game, "GAME.md", GameMd);
        TempFactoryRoot.AddFile(game, "backlog/004-feel-check-jump-arc.md", FeelStory);
        TempFactoryRoot.AddFile(game, "backlog/005-pick-a-name.md", "# Pick a name\n\n## What\n\nName it.\n");
        TempFactoryRoot.AddFile(game, "playtest/baseline.json", Baseline);
        TempFactoryRoot.AddFile(root.Path, "factory/checks/Hopper.json", Check);
        return game;
    }

    /// <summary>Adds the review job with two rounds, records, bots files and screenshots (the game is added by
    /// <see cref="AddGame"/>).</summary>
    public static void AddJob(TempFactoryRoot root)
    {
        root.AddJob(JobId, JobState);
        root.AddRecord(JobId, "spec.md", "# Jump arc\n\n## Playtest targets\n\n- completionRate (greedy): 0.6–0.9\n");
        root.AddRecord(JobId, "spec-review.md", "VERDICT: PASS\n");
        root.AddRecord(JobId, "build.md", "# Build\n\nDone.\n");
        root.AddRecord(JobId, "round-1/playtest.md", "PLAYTEST: FAIL\n");
        root.AddRecord(JobId, "round-1/bots-greedy.json", BotsRound1);
        root.AddRecord(JobId, "round-1/review-gameplay.md", "VERDICT: CHANGES\n");
        root.AddRecord(JobId, "round-2/playtest.md", "PLAYTEST: PASS\n");
        root.AddRecord(JobId, "round-2/review-gameplay.md", "VERDICT: PASS\n");
        root.AddRecord(JobId, "round-2/review-art.md", "VERDICT: CHANGES\n");
        root.AddRecord(JobId, "decision.md", "ESCALATE\n");
        root.AddShot(JobId, 2, "start-first-frame.png", Png);
        root.AddShot(JobId, 2, "level-1.png", Png);
    }

    private static byte[] MakePng()
    {
        using MemoryStream png = new();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), 1);
        header[8] = 8;
        header[9] = 2;
        Chunk(png, "IHDR", header);
        using MemoryStream raw = new();
        using (ZLibStream zlib = new(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write([0, 200, 100, 50]);
        }

        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string kind, byte[] data)
    {
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        byte[] typed = [.. Encoding.ASCII.GetBytes(kind), .. data];
        png.Write(typed);
        byte[] crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
        png.Write(crc);
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }
}
