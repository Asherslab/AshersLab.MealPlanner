namespace AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Server;

/// <summary>
/// What the server says about itself. The scaffold's end-to-end call: if a page can show this, the
/// browser reached the gRPC host over gRPC-Web and the host reached its SQLite database.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public record ServerInfo
{
    /// <summary>The host assembly's informational version. CI stamps the semantic version here.</summary>
    public required string Version { get; init; }

    public required DateTime ServerTimeUtc { get; init; }

    /// <summary>
    /// The newest EF migration applied to the server database, read from the database itself rather than
    /// from the assembly. Null only if the database has never been migrated, which startup prevents.
    /// </summary>
    public string? LatestMigration { get; init; }

    /// <summary>
    /// The Home Assistant login name the call arrived with, from ingress. Null outside Home Assistant (local
    /// development), where nobody is signed in.
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>The same user's display name in Home Assistant, when it has one.</summary>
    public string? UserDisplayName { get; init; }
}
