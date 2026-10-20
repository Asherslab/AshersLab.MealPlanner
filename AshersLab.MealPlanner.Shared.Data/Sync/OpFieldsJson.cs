using System.Text.Json;
using System.Text.Json.Serialization;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Sync;

/// <summary>
/// An op's fields as stored in the browser's outbox and the server's log: one JSON column, so every entity's
/// ops fit one table.
/// </summary>
/// <remarks>
/// Source-generated, not reflection: the WASM app is trimmed when published, and reflection-based
/// serialisation of a type nothing else references can be trimmed away.
/// </remarks>
public static class OpFieldsJson
{
    public static string Write(IReadOnlyList<OpField> fields) =>
        JsonSerializer.Serialize([.. fields], OpFieldsJsonContext.Default.ListOpField);

    public static List<OpField> Read(string json) =>
        JsonSerializer.Deserialize(json, OpFieldsJsonContext.Default.ListOpField) ?? [];
}

[JsonSerializable(typeof(List<OpField>))]
internal sealed partial class OpFieldsJsonContext : JsonSerializerContext;
