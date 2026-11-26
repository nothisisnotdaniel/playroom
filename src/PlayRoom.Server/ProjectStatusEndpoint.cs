namespace PlayRoom.Server;

/// <summary>One project's status: the commits on <c>develop</c> not on <c>main</c> (or <c>null</c> when unknown).
/// GameFactory records no CI result in a job, so there is none here.</summary>
internal sealed record ProjectStatus(string Name, int? Ahead);

/// <summary>The <c>GET /api/status</c> response.</summary>
internal sealed record ProjectStatusResponse(IReadOnlyList<ProjectStatus> Projects);

/// <summary><c>GET|HEAD /api/status</c>: per discovered project (in <see cref="ProjectFinder"/> order)
/// <see cref="ProjectGit.AheadOfMain"/>. Read-only, <c>no-store</c>; any query parameter gives
/// <c>400 {"error":"invalid"}</c>. Runs only the one fixed git call per project, never <c>gh</c> or the
/// network.</summary>
internal static class ProjectStatusEndpoint
{
    public const string Route = "/api/status";

    public const string InvalidBody = "{\"error\":\"invalid\"}";

    public static void Map(WebApplication app)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], async (FactoryRoot root, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Query.Count > 0)
            {
                return Results.Content(InvalidBody, "application/json", statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Json(await Build(root.Path));
        });
    }

    public static async Task<ProjectStatusResponse> Build(string factoryRoot)
    {
        IReadOnlyList<ProjectFolder> projects = ProjectFinder.Find(factoryRoot);
        List<ProjectStatus> statuses = [];
        foreach (ProjectFolder project in projects)
        {
            int? ahead = await ProjectGit.AheadOfMain(project.Path);
            statuses.Add(new ProjectStatus(project.Name, ahead));
        }

        return new ProjectStatusResponse(statuses);
    }
}
