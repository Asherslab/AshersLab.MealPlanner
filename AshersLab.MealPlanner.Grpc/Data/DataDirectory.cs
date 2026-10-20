namespace AshersLab.MealPlanner.Grpc.Data;

/// <summary>
/// Where the server keeps its files: today the SQLite database, later Litestream's state.
/// </summary>
/// <remarks>
/// One configuration key, <c>DataDirectory</c>, rather than a connection string, because the thing that
/// differs between deployments is the directory, not the database settings: <c>/data</c> in the Home
/// Assistant add-on (which is what puts it in HA backups), a PVC mount on Kubernetes, and a folder under
/// the user's Application Support locally, set by the AppHost. A full <c>ConnectionStrings:mealplanner</c>
/// still wins if one is given.
/// <para>
/// The relative default exists so a bare <c>dotnet ef</c> or a test host has somewhere to write; nothing
/// deployed should rely on it.
/// </para>
/// </remarks>
public static class DataDirectory
{
    public const string ConfigKey = "DataDirectory";
    public const string ConnectionStringName = "mealplanner";
    public const string DatabaseFileName = "mealplanner.db";

    public static string ConnectionString(IConfiguration configuration)
    {
        if (configuration.GetConnectionString(ConnectionStringName) is { Length: > 0 } explicitConnection)
            return explicitConnection;

        // "app-data", not "data": macOS is case-insensitive, and "data" is this project's Data/ source folder.
        string directory = Path.GetFullPath(configuration[ConfigKey] is { Length: > 0 } configured ? configured : "app-data");

        // SQLite creates the file but not the folder it goes in; without this the first start on a fresh
        // machine fails with "SQLite Error 14: 'unable to open database file'".
        Directory.CreateDirectory(directory);

        return $"Data Source={Path.Combine(directory, DatabaseFileName)}";
    }
}
