using System.Reflection;

namespace PlayRoom.Server;

/// <summary>PlayRoom's values for RoomKit: names, port, release repository, capture note, version and the
/// GameFactory job format.</summary>
internal static class PlayRoomApp
{
    /// <summary>The app's options. The version is this assembly's informational version (<c>dev</c> without
    /// <c>-p:Version</c>), never RoomKit's. Releases come from the GitHub repository
    /// <c>nothisisnotdaniel/playroom</c>.</summary>
    public static RoomKitOptions Options { get; } = new()
    {
        AppName = "PlayRoom",
        ShortName = "PlayRoom",
        DataFolderName = "PlayRoom",
        SolutionFileName = "PlayRoom.slnx",
        DefaultPort = 8110,
        ReleaseRepository = "nothisisnotdaniel/playroom",
        CaptureNote = "Draft from PlayRoom",
        InformationalVersion = typeof(PlayRoomApp).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    };

    /// <summary>GameFactory's job format.</summary>
    public static IJobFormat Jobs { get; } = new GameJobFormat();

    /// <summary>Maps PlayRoom's own endpoints: project status, releases, board, changes, job, shot, games and
    /// assets. All are read-only.</summary>
    public static void Map(RoomRoutes routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ProjectStatusEndpoint.Map(routes.App);
        ReleasesEndpoint.Map(routes.App);
        BoardEndpoint.Map(routes.App);
        ChangesEndpoint.Map(routes.App);
        JobEndpoint.Map(routes.App);
        ShotEndpoint.Map(routes.App);
        GamesEndpoint.Map(routes.App);
        AssetsEndpoint.Map(routes.App);
    }
}
