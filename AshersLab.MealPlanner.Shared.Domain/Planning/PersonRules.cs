using System.Text.RegularExpressions;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Planning;

/// <summary>
/// Someone in the household, as the op log sees it: a name and a colour that marks their plan entries. <b>Never
/// seeded</b> - names are personal and the repo is public; the planner adds the household in the app.
/// </summary>
public static partial class PersonRules
{
    /// <summary>The op log's <c>Entity</c> for people. On the wire and in both databases - never rename.</summary>
    public const string Entity = "Person";

    public const string NameField = "Name";

    /// <summary>"#rrggbb", lower case - one spelling per colour.</summary>
    public const string ColourField = "Colour";

    public const string Noun = "A person";

    /// <summary>
    /// What the editor offers, in order: distinct from each other and readable as a chip's background with white text.
    /// The first unused one is a new person's.
    /// </summary>
    public static readonly IReadOnlyList<string> Palette =
        ["#1e88e5", "#e53935", "#43a047", "#8e24aa", "#fb8c00", "#00897b", "#6d4c41", "#546e7a"];

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal) { NameField, ColourField };

    private static readonly string[] RequiredOnCreate = [NameField, ColourField];

    [GeneratedRegex("^#[0-9a-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColourShape();

    public static string? NameProblem(string name) => Names.Problem(name, Noun);

    public static string? ColourProblem(string? colour) =>
        colour is not null && ColourShape().IsMatch(colour) ? null : "The colour is not one like #1e88e5.";

    /// <summary>The colour for someone new: the first in the palette nobody has, or the first when all are taken.</summary>
    public static string NextColour(IEnumerable<string> taken)
    {
        HashSet<string> used = [.. taken];
        return Palette.FirstOrDefault(c => !used.Contains(c)) ?? Palette[0];
    }

    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            NameField   => Names.FieldProblem(field.Value, Noun),
            ColourField => ColourProblem(field.Value),
            _           => null,
        });
}
