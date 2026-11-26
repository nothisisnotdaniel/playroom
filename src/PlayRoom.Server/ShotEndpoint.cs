using System.Globalization;
using Microsoft.Extensions.Primitives;

namespace PlayRoom.Server;

/// <summary><c>GET /api/shot?id=&lt;job&gt;&amp;round=&lt;n&gt;&amp;name=&lt;file&gt;</c>: one PNG from a job's
/// <c>round-&lt;n&gt;/shots/</c>. The id must be a listed job folder (exact match), the round a whole number from 1
/// to <see cref="GameJobReader.MaxRounds"/> and the name one of <see cref="PlaytestView.Shots"/> for that round, so
/// no request value is joined into a path unchecked. The file is read no-follow with a size cap and must start
/// with the PNG signature. Every refusal is the same fixed 404.</summary>
internal static class ShotEndpoint
{
    public const string Route = "/api/shot";

    public const long MaxShotBytes = 8 * 1024 * 1024;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static void Map(WebApplication app)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], (FactoryRoot root, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            byte[]? png = Read(root.Path, context.Request.Query["id"], context.Request.Query["round"], context.Request.Query["name"]);
            if (png is null)
            {
                return Results.Content(JobEndpoint.NotFoundBody, "application/json", statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Bytes(png, "image/png");
        });
    }

    /// <summary>The PNG bytes, or <c>null</c> for any refusal.</summary>
    public static byte[]? Read(string factoryRoot, StringValues id, StringValues round, StringValues name)
    {
        if (id.Count != 1 || round.Count != 1 || name.Count != 1
            || !int.TryParse(round[0], NumberStyles.None, CultureInfo.InvariantCulture, out int n)
            || n < 1 || n > GameJobReader.MaxRounds
            || round[0]!.StartsWith('0'))
        {
            return null;
        }

        JobFolder? folder = GameJobReader.Find(factoryRoot, id[0]);
        if (folder is null)
        {
            return null;
        }

        string roundPath = Path.Combine(folder.Path, "round-" + n.ToString(CultureInfo.InvariantCulture));
        if (!PlaytestView.IsPlainFolder(roundPath))
        {
            return null;
        }

        string? listed = PlaytestView.Shots(roundPath).FirstOrDefault(shot => string.Equals(shot, name[0], StringComparison.Ordinal));
        if (listed is null)
        {
            return null;
        }

        byte[]? bytes = SafeFileReader.ReadBytes(Path.Combine(roundPath, "shots", listed), MaxShotBytes);
        return bytes is not null && bytes.AsSpan().StartsWith(PngSignature) ? bytes : null;
    }
}
