using System.Text.Json;
using AshersLab.MealPlanner.Grpc.Data;

namespace AshersLab.MealPlanner.Grpc.Features.Auth;

/// <summary>
/// The Home Assistant add-on's options (its Configuration tab), which the Supervisor writes to <c>/data/options.json</c>
/// - not to the environment. Read into configuration so the owner sets the sign-in proxy's address there, after looking
/// up where Nginx Proxy Manager connects from (docs/modules/infrastructure/deployment.md).
/// </summary>
public static class AddOnOptions
{
    public const string FileName = "options.json";

    /// <summary>Each option the add-on's <c>schema</c> declares, and the configuration key it sets.</summary>
    public static readonly IReadOnlyDictionary<string, string> Keys = new Dictionary<string, string>
    {
        ["trusted_proxies"]     = $"{AuthOptions.Section}:{nameof(AuthOptions.TrustedProxies)}",
        ["user_header"]         = $"{AuthOptions.Section}:{nameof(AuthOptions.UserHeader)}",
        ["display_name_header"] = $"{AuthOptions.Section}:{nameof(AuthOptions.DisplayNameHeader)}",
    };

    /// <summary>The options file's settings as configuration keys. Options left empty set nothing; others are ignored.</summary>
    public static IReadOnlyDictionary<string, string?> Read(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Dictionary<string, string?> settings = [];
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return settings;
        foreach (JsonProperty option in document.RootElement.EnumerateObject())
            if (Keys.TryGetValue(option.Name, out string? key) && option.Value.ValueKind == JsonValueKind.String
                && option.Value.GetString() is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value))
                settings[key] = value.Trim();
        return settings;
    }

    /// <summary>Adds the options file in the data directory, when there is one, over everything else.</summary>
    public static void AddTo(ConfigurationManager configuration)
    {
        string path = Path.Combine(Path.GetDirectoryName(DataDirectory.DatabasePath(configuration))!, FileName);
        if (File.Exists(path))
            configuration.AddInMemoryCollection(Read(File.ReadAllText(path)));
    }
}
