using System.Text.Json;
using AshersLab.MealPlanner.Grpc.Data;

namespace AshersLab.MealPlanner.Grpc.Features.Auth;

/// <summary>
/// The Home Assistant add-on's options (its Configuration tab), which the Supervisor writes to <c>/data/options.json</c>
/// - not to the environment. Read into configuration so the owner sets the app's public address there, and can ask for a
/// new setup link when nobody can sign in (docs/modules/infrastructure/accounts.md).
/// </summary>
public static class AddOnOptions
{
    public const string FileName = "options.json";

    /// <summary>Each option the add-on's <c>schema</c> declares, and the configuration key it sets.</summary>
    public static readonly IReadOnlyDictionary<string, string> Keys = new Dictionary<string, string>
    {
        ["public_url"]   = $"{AuthOptions.Section}:{nameof(AuthOptions.PublicUrl)}",
        ["reset_access"] = $"{AuthOptions.Section}:{nameof(AuthOptions.ResetAccess)}",
    };

    /// <summary>
    /// The options file's settings as configuration keys: text and true/false. Options left empty set nothing; any other
    /// option - including anything naming the mode - is ignored, so nothing on the Configuration tab can turn sign-in off.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Read(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Dictionary<string, string?> settings = [];
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return settings;
        foreach (JsonProperty option in document.RootElement.EnumerateObject())
        {
            if (!Keys.TryGetValue(option.Name, out string? key))
                continue;
            string? value = option.Value.ValueKind switch
            {
                JsonValueKind.String => option.Value.GetString()?.Trim(),
                JsonValueKind.True   => "true",
                JsonValueKind.False  => "false",
                _                    => null,
            };
            if (value is { Length: > 0 })
                settings[key] = value;
        }
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
