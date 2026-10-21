using System.Text;

namespace AshersLab.MealPlanner.Shared.Domain.Import;

/// <summary>How well a name from the sheet matches one already known.</summary>
public enum MatchStrength
{
    None,

    /// <summary>Close enough to suggest, not enough to take without asking: "cheese" for "Shredded cheese".</summary>
    Possible,

    /// <summary>Probably the same, spelt differently: "Lmab korma" for "Lamb korma". Still asked, with it chosen.</summary>
    Likely,

    /// <summary>The same once case, spacing, punctuation and plurals are ignored: "egg" and "Eggs". Taken without asking.</summary>
    Same,
}

/// <param name="Inside">Every word of one name is in the other: "Beans &amp; rice" in "Frozen beans &amp; rice".</param>
public sealed record NameCandidate<T>(T Item, string Name, MatchStrength Strength, decimal Score, bool Inside = false);

/// <summary>
/// The sheet's names, compared the way the planner reads them. A sheet can have "pears" and "Pears", "egg" and
/// "Eggs", "beef strips " twice and "Popcorn?" - one ingredient each.
/// </summary>
public static class ImportNames
{
    /// <summary>What two names that are "the same" share: lower case, letters and digits only, each word singular.</summary>
    public static string Key(string name)
    {
        StringBuilder flat = new();
        foreach (char c in name.ToLowerInvariant())
            flat.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return string.Join(' ', flat.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Singular));
    }

    /// <summary>
    /// Plurals by spelling, not a dictionary: both sides of a comparison go through it, so "pouches" and "pouch" meet
    /// at "pouch" even where the rule is wrong English.
    /// </summary>
    private static string Singular(string word) => word switch
    {
        { Length: > 4 } when word.EndsWith("ies", StringComparison.Ordinal)                                   => word[..^3] + "y",
        { Length: > 4 } when word.EndsWith("oes", StringComparison.Ordinal)                                   => word[..^2],
        { Length: > 4 } when word.EndsWith("ches", StringComparison.Ordinal) || word.EndsWith("shes", StringComparison.Ordinal) => word[..^2],
        { Length: > 3 } when word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal)             => word[..^1],
        _                                                                                                       => word,
    };

    /// <summary>
    /// A name as the import saves it: normalised, a trailing "?" dropped (the planner's "not sure", kept in the meal's
    /// notes instead), and a first letter capitalised when the sheet wrote it all in lower case - the shopping
    /// categories write "Bacon", the meal ideas "bacon", and the catalogue should read one way.
    /// </summary>
    public static string Tidy(string text)
    {
        string name = Names.Normalise(text).TrimEnd('?', ' ');
        if (name.Length > 0 && name == name.ToLowerInvariant())
            name = char.ToUpperInvariant(name[0]) + name[1..];
        return name.Length > Names.MaxLength ? name[..Names.MaxLength].TrimEnd() : name;
    }

    /// <summary>
    /// The known names <paramref name="name"/> could be, best first. <see cref="MatchStrength.Same"/> only for equal
    /// keys; <see cref="MatchStrength.Likely"/> for one or two letters wrong or swapped in a long enough name;
    /// <see cref="MatchStrength.Possible"/> when every word of one is in the other ("cheese", "Smoked cheese"), or they
    /// share a word of four letters or more ("roast pumpkin", "Roast potatoes"). Among the possible, a name wholly inside
    /// the other comes first: "Beans & rice" before "Frozen kale" for "Frozen beans & rice", however close the spelling.
    /// </summary>
    public static List<NameCandidate<T>> Candidates<T>(string name, IEnumerable<T> known, Func<T, string> nameOf, int max = 5)
    {
        string key = Key(name);
        List<NameCandidate<T>> found = [];
        foreach (T item in known)
        {
            string other = Key(nameOf(item));
            if (other.Length == 0 || key.Length == 0)
                continue;
            if (other == key)
            {
                found.Add(new(item, nameOf(item), MatchStrength.Same, 1m));
                continue;
            }

            int     distance = Distance(key, other);
            decimal score    = 1m - (decimal)distance / Math.Max(key.Length, other.Length);
            bool    inside   = Words(key).IsSubsetOf(Words(other)) || Words(other).IsSubsetOf(Words(key));
            MatchStrength strength =
                score >= 0.8m && distance <= 2 && Math.Min(key.Length, other.Length) >= 5 ? MatchStrength.Likely
                : inside ? MatchStrength.Possible
                : score >= 0.75m ? MatchStrength.Possible
                : Words(key).Intersect(Words(other)).Any(w => w.Length >= 4) ? MatchStrength.Possible
                : MatchStrength.None;
            if (strength != MatchStrength.None)
                found.Add(new(item, nameOf(item), strength, score, inside));
        }

        return [.. found
            .OrderByDescending(c => c.Strength)
            .ThenByDescending(c => c.Inside)
            .ThenByDescending(c => c.Score)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)];
    }

    private static HashSet<string> Words(string key) => [.. key.Split(' ')];

    /// <summary>Edits between two strings, a swap of neighbours counting as one: "lmab" to "lamb" is 1.</summary>
    public static int Distance(string a, string b)
    {
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
            d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++)
            d[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }
        return d[a.Length, b.Length];
    }
}
