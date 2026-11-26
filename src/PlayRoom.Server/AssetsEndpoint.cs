using Microsoft.Extensions.Primitives;

namespace PlayRoom.Server;

/// <summary>One asset file and its ledger line, if any.</summary>
internal sealed record AssetRow(string Path, long? Bytes, LedgerRow? Ledger, bool LicenceOk, bool SizeOk);

/// <summary>The <c>GET /api/assets</c> response: the assets folder, the allowed licences, every file under it with
/// its ledger line, and the three lists of <c>job.py ledger-check</c>.</summary>
internal sealed record AssetsResponse(
    string Project,
    string Folder,
    bool FromGit,
    IReadOnlyList<string> Licences,
    IReadOnlyList<AssetRow> Files,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> BadLicence,
    IReadOnlyList<string> WrongSize,
    IReadOnlyList<LedgerRow> Unused);

/// <summary><c>GET /api/assets?project=&lt;G&gt;</c>: the asset ledger of one game, checked as
/// <c>job.py ledger-check</c> checks it: every tracked or untracked, non-ignored file under the <c>assets:</c>
/// folder needs an <c>ASSETS.md</c> line with an allowed licence and its size. The files come from
/// <c>git ls-files</c> on what is checked out; without git, from a no-follow walk of the folder. The project is
/// matched against the listed games, never joined into a path. Every refusal is the same fixed 404.</summary>
internal static class AssetsEndpoint
{
    public const string Route = "/api/assets";

    public const int MaxFiles = 5000;

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(20);

    public static void Map(WebApplication app)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], async (FactoryRoot root, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            AssetsResponse? assets = await Build(root.Path, context.Request.Query["project"]);
            if (assets is null)
            {
                return Results.Content(JobEndpoint.NotFoundBody, "application/json", statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Json(assets);
        });
    }

    public static async Task<AssetsResponse?> Build(string factoryRoot, StringValues projectValue)
    {
        if (projectValue.Count != 1)
        {
            return null;
        }

        ProjectFolder? game = GamesEndpoint.Games(factoryRoot)
            .FirstOrDefault(g => string.Equals(g.Name, projectValue[0], StringComparison.Ordinal));
        if (game is null)
        {
            return null;
        }

        string folder = AssetsFolder(GameFiles.AdapterKeys(game.Path).GetValueOrDefault("assets"));
        IReadOnlyList<string> licences = GameFiles.Licences(GameFiles.Read(game.Path, "GAME.md"));
        Dictionary<string, LedgerRow> ledger = GameFiles.Ledger(GameFiles.Read(game.Path, "ASSETS.md"))
            .ToDictionary(row => row.Path, StringComparer.Ordinal);
        (IReadOnlyList<string> files, bool fromGit) = await Files(game.Path, folder);

        List<AssetRow> rows = [];
        List<string> missing = [];
        List<string> badLicence = [];
        List<string> wrongSize = [];
        foreach (string file in files)
        {
            long? bytes = Size(game.Path, file);
            if (bytes is null)
            {
                continue;
            }

            if (!ledger.TryGetValue(file, out LedgerRow? row))
            {
                missing.Add(file);
                rows.Add(new AssetRow(file, bytes, null, false, false));
                continue;
            }

            bool licenceOk = licences.Contains(row.Licence, StringComparer.Ordinal);
            bool sizeOk = row.Bytes == bytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!licenceOk)
            {
                badLicence.Add(file + " (" + row.Licence + ")");
            }

            if (!sizeOk)
            {
                wrongSize.Add(file + " (" + row.Bytes + " in ASSETS.md, " + bytes.Value + " on disk)");
            }

            rows.Add(new AssetRow(file, bytes, row, licenceOk, sizeOk));
        }

        HashSet<string> present = [.. rows.Select(r => r.Path)];
        return new AssetsResponse(game.Name, folder, fromGit, licences, rows, missing, badLicence, wrongSize,
            [.. ledger.Values.Where(row => !present.Contains(row.Path))]);
    }

    /// <summary>The <c>assets:</c> value without backticks and a trailing slash; <c>assets</c> when empty. A value
    /// that leaves the game (absolute, <c>..</c>) falls back to <c>assets</c>.</summary>
    public static string AssetsFolder(string? value)
    {
        string folder = (value ?? "").Trim().Trim('`').Trim().TrimEnd('/');
        if (folder.Length == 0 || Path.IsPathRooted(folder) || folder.Contains('\\')
            || folder.Split('/').Any(part => part is "" or "." or ".."))
        {
            return "assets";
        }

        return folder;
    }

    /// <summary>The asset files relative to the game, sorted (ordinal), from git when it answers, else from a
    /// walk of the folder; at most <see cref="MaxFiles"/>.</summary>
    private static async Task<(IReadOnlyList<string> Files, bool FromGit)> Files(string gamePath, string folder)
    {
        GitResult tracked = await ProjectGit.Run(gamePath, ["ls-files", "-z", "--", folder], GitTimeout);
        GitResult others = await ProjectGit.Run(gamePath, ["ls-files", "-z", "--others", "--exclude-standard", "--", folder], GitTimeout);
        if (tracked.ExitCode == 0 && others.ExitCode == 0)
        {
            SortedSet<string> files = new(StringComparer.Ordinal);
            foreach (string file in (tracked.Output + "\0" + others.Output).Split('\0'))
            {
                if (file.Length > 0 && files.Count < MaxFiles)
                {
                    files.Add(file);
                }
            }

            return ([.. files], true);
        }

        return (Walk(gamePath, folder), false);
    }

    /// <summary>The regular files under the folder, never through a symlink, skipping dot-names.</summary>
    private static List<string> Walk(string gamePath, string folder)
    {
        List<string> files = [];
        Stack<string> pending = new([folder]);
        while (pending.Count > 0 && files.Count < MaxFiles)
        {
            string relative = pending.Pop();
            string full = Path.Combine(gamePath, relative);
            if (!PlaytestView.IsPlainFolder(full))
            {
                continue;
            }

            try
            {
                foreach (FileSystemInfo entry in new DirectoryInfo(full).EnumerateFileSystemInfos())
                {
                    if (entry.Name.StartsWith('.') || entry.LinkTarget is not null)
                    {
                        continue;
                    }

                    string child = relative + "/" + entry.Name;
                    if (entry is DirectoryInfo)
                    {
                        pending.Push(child);
                    }
                    else if (files.Count < MaxFiles)
                    {
                        files.Add(child);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>The size of a regular file of the game that is not a symlink, else <c>null</c>.</summary>
    private static long? Size(string gamePath, string file)
    {
        try
        {
            FileInfo info = new(Path.Combine(gamePath, file));
            return info.Exists && info.LinkTarget is null ? info.Length : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
