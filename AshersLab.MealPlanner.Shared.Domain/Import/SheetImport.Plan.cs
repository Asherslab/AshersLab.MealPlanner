using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Import;

/// <summary>What the sheet's plan adds: a template of <paramref name="Weeks"/> weeks, its entries, and the slots and meals it needed.</summary>
public sealed record PlanImportCounts(int Weeks, int Entries, int NewSlots, int NewMeals);

/// <summary>
/// The sheet's "WEEK 1".."WEEK n" plan, as a plan template. Its names are matched against the meals - the app's, and
/// the meal ideas this run adds - and only an exact match is taken without asking.
/// </summary>
/// <remarks>
/// <b>The plan is imported once.</b> The template has a fixed id (<see cref="PlanTemplateId"/>), so a second run - or
/// a later download of the sheet - finds it, renamed or not, and imports no plan at all; one the planner deleted is a
/// tombstone with that id, and stays deleted. After the first run the app is the master of the plan, as it is of
/// everything else the sheet gave.
/// </remarks>
public sealed partial class SheetImport
{
    /// <summary>
    /// The sheet's plan template, always this id: importing again - on this device or another, even offline at the same
    /// time - finds it rather than making a second. Not a seed; only the import writes it.
    /// </summary>
    public static readonly Guid PlanTemplateId = new("00000000-5eed-7000-8000-000000000501");

    public const string PlanTemplateName = "Spreadsheet plan";

    /// <summary>Plan words that are a kind of meal, not a recipe: made once, on first sight, if no meal has the name.</summary>
    private static readonly IReadOnlyList<(string Key, string Name, MealKind Kind)> SpecialMeals =
    [
        (ImportNames.Key("Leftovers"), "Leftovers", MealKind.Leftovers),
        (ImportNames.Key("Family dinner"), "Family dinner", MealKind.NoCost),
    ];

    private bool PlanWillImport => _sheet.Plan.Count > 0 && _known.Templates.All(t => t.Id != PlanTemplateId);

    /// <summary>Every meal a plan name can be, by key: the app's live meals, then the meal ideas this run adds.</summary>
    private Dictionary<string, string> MealPool()
    {
        Dictionary<string, string> pool = new(StringComparer.Ordinal);
        foreach (KnownNamed meal in _known.Meals.Where(m => !m.Deleted).OrderBy(m => m.Id))
            pool.TryAdd(ImportNames.Key(meal.Name), meal.Name);
        foreach (MealIdeaCell idea in NewMealIdeas())
            pool.TryAdd(ImportNames.Key(idea.Idea.Name), idea.Idea.Name);
        pool.Remove("");
        return pool;
    }

    /// <summary>
    /// Meals deleted here, and not also live under the same key, by key: a plan name for one is left out, not remade - and
    /// one only like it ("Nodles") is asked about with the deleted meal offered and chosen.
    /// </summary>
    private Dictionary<string, string> GoneMeals(Dictionary<string, string> pool)
    {
        Dictionary<string, string> gone = new(StringComparer.Ordinal);
        foreach (KnownNamed meal in _known.Meals.Where(m => m.Deleted).OrderBy(m => m.Id))
        {
            string key = ImportNames.Key(meal.Name);
            if (key.Length > 0 && !pool.ContainsKey(key))
                gone.TryAdd(key, meal.Name);
        }
        return gone;
    }

    /// <summary>How a deleted meal reads among a plan question's options: choosing it leaves the name out.</summary>
    public static string GoneOption(string name) => $"{name} - deleted here, leave it out";

    /// <summary>
    /// The names one choice of a cell plans, as few as match: the whole choice if it is a meal ("Fish, chips and
    /// peas"), else each ','-part that is one ("Eggs + soldiers"), else that part's '+'-items.
    /// </summary>
    private static List<string> PlanNames(PlanChoiceText choice, Func<string, bool> known)
    {
        if (known(choice.Text))
            return [choice.Text];
        List<string> names = [];
        foreach (PlanPartText part in choice.Parts)
        {
            if (known(part.Text))
                names.Add(part.Text);
            else
                names.AddRange(part.Items);
        }
        return names;
    }

    private static Func<string, bool> KnownTo(Dictionary<string, string> pool, Dictionary<string, string> gone) => name =>
    {
        string key = ImportNames.Key(name);
        return pool.ContainsKey(key) || gone.ContainsKey(key) || SpecialMeals.Any(s => s.Key == key);
    };

    private IEnumerable<PlanCell> PlanCells() =>
        _sheet.Plan.SelectMany(w => w.Rows).SelectMany(r => r.Days).OfType<PlanCell>();

    /// <summary>
    /// A cell's names, by choice; null when it is a choice between several things eaten together ("Takeaway, salad/soup"),
    /// which a plan entry - one meal from its choices - cannot hold.
    /// </summary>
    private static List<List<string>>? CellNames(PlanCell cell, Func<string, bool> known)
    {
        List<List<string>> choices = [.. SheetText.PlanChoices(cell.Original).Select(c => PlanNames(c, known))];
        return choices.Count > 1 && choices.Any(c => c.Count > 1) ? null : choices;
    }

    /// <summary>The plan's names that are not exactly a meal but resemble one. Nothing like any meal: a new one, not asked.</summary>
    private List<PlanMealQuestion> PlanMealQuestions()
    {
        if (!PlanWillImport)
            return [];
        Dictionary<string, string> pool  = MealPool();
        Dictionary<string, string> gone  = GoneMeals(pool);
        Func<string, bool>         known = KnownTo(pool, gone);
        // A deleted meal is an option too, so a typo of one is not quietly made into a new meal.
        List<(string Key, string Name)> options = [.. pool.Select(p => (p.Key, p.Value)), .. gone.Select(g => (g.Key, GoneOption(g.Value)))];

        Dictionary<string, (string Name, List<SheetPlace> Places)> unknown = new(StringComparer.Ordinal);
        foreach (PlanCell cell in PlanCells())
        {
            foreach (string name in (CellNames(cell, known) ?? []).SelectMany(c => c).Where(n => !known(n)))
            {
                string key = ImportNames.Key(name);
                if (key.Length == 0)
                    continue;
                if (!unknown.TryGetValue(key, out (string Name, List<SheetPlace> Places) seen))
                    unknown[key] = seen = (name, []);
                if (cell.Place is { } place && !seen.Places.Contains(place))
                    seen.Places.Add(place);
            }
        }

        List<PlanMealQuestion> questions = [];
        foreach ((string key, (string name, List<SheetPlace> places)) in unknown)
        {
            List<NameCandidate<(string Key, string Name)>> candidates =
                ImportNames.Candidates(name, options, p => gone.GetValueOrDefault(p.Key) ?? p.Name);
            if (candidates.Count == 0)
                continue;
            questions.Add(new PlanMealQuestion(key, ImportNames.Tidy(name), places, [.. candidates.Select(c => c.Item)],
                candidates.FirstOrDefault(c => c.Strength >= MatchStrength.Likely)?.Item.Key));
        }
        return questions;
    }

    /// <summary>
    /// The plan's edits: the template, any slot the day does not have yet, a meal for every name that is none yet, and an
    /// entry per thing eaten with its choices. Appended to <paramref name="edits"/>, after the meals.
    /// </summary>
    private PlanImportCounts ImportPlanTemplate(
        ImportChoices choices, Dictionary<string, Target> ingredients, Dictionary<string, Guid> newMealIdeas,
        List<ImportEdit> edits, List<string> leftOut, ref int newMeals, ref int newLines, Func<Guid> newId)
    {
        PlanImportCounts none = new(0, 0, 0, 0);
        if (_sheet.Plan.Count == 0)
            return none;
        if (_known.Templates.FirstOrDefault(t => t.Id == PlanTemplateId) is { } here)
        {
            leftOut.Add(here.Deleted
                ? "The plan: deleted here before, so not imported again."
                : $"The plan: already here as \"{here.Name}\", so not imported again - change it on the Plans page.");
            return none;
        }

        Dictionary<string, string> pool  = MealPool();
        Dictionary<string, string> gone  = GoneMeals(pool);
        Func<string, bool>         known = KnownTo(pool, gone);
        Dictionary<string, string?> suggested = PlanMealQuestions().ToDictionary(q => q.Key, q => q.Suggested, StringComparer.Ordinal);

        // Every meal a name can already be, by key: the app's, then this run's meal ideas.
        Dictionary<string, Guid> meals = new(StringComparer.Ordinal);
        foreach (KnownNamed meal in _known.Meals.Where(m => !m.Deleted).OrderBy(m => m.Id))
            meals.TryAdd(ImportNames.Key(meal.Name), meal.Id);
        foreach ((string key, Guid id) in newMealIdeas)
            meals.TryAdd(key, id);

        int createdMeals = 0;
        HashSet<string> saidGone = new(StringComparer.Ordinal);

        Guid? MealFor(string name, SheetPlace? place, ref int lines)
        {
            string key = ImportNames.Key(name);
            if (key.Length == 0)
                return null;
            if (meals.TryGetValue(key, out Guid id))
                return id;
            string? answer = choices.PlanMeals.TryGetValue(key, out string? chosen) ? chosen : suggested.GetValueOrDefault(key);
            if (gone.ContainsKey(key) || (answer is not null && gone.ContainsKey(answer)))
            {
                if (saidGone.Add(key))
                    leftOut.Add(gone.TryGetValue(key, out _)
                        ? $"{ImportNames.Tidy(name)} ({place}): that meal was deleted here, so it is left out of the plan."
                        : $"{ImportNames.Tidy(name)} ({place}): read as {gone[answer!]}, which was deleted here, so it is left out of the plan.");
                return null;
            }
            if (answer is not null && meals.TryGetValue(answer, out Guid same))
                return meals[key] = same;

            (string Name, MealKind Kind)? special = SpecialMeals.Where(s => s.Key == key).Select(s => ((string, MealKind)?)(s.Name, s.Kind)).FirstOrDefault();
            Guid meal = newId();
            edits.Add(new(MealRules.Entity, meal, OpKind.Create,
                new MealDraft(special?.Name ?? ImportNames.Tidy(name), null, special?.Kind ?? MealKind.Simple, 1, null, null).FieldsChangedFrom(null)));
            createdMeals++;
            // A single-serve meal named as an ingredient is made of it, as a meal idea with no parentheses is.
            if (special is null && ingredients.TryGetValue(key, out Target? ingredient))
            {
                edits.Add(new(MealIngredientRules.Entity, newId(), OpKind.Create,
                    new MealIngredientDraft(meal, ingredient.Id, null, null).FieldsChangedFrom(null)));
                lines++;
            }
            return meals[key] = meal;
        }

        // Slots: a row's leading word, the nth row of that word in a week being the nth slot of that name - the day has
        // two snacks. One the day does not have is added at the end; one deleted here leaves its rows out.
        List<KnownOrdered> slots     = [.. _known.Slots.Where(s => !s.Deleted).OrderBy(s => s.Position).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id)];
        List<decimal>      positions = [.. slots.Select(s => s.Position)];
        Dictionary<(string, int), Guid?> slotIds = [];
        int newSlots = 0;
        HashSet<string> saidLabel = new(StringComparer.OrdinalIgnoreCase);

        Guid? SlotFor(string label, string word, int nth)
        {
            string name = ImportNames.Tidy(word);
            (string, int) at = (name.ToLowerInvariant(), nth);
            if (slotIds.TryGetValue(at, out Guid? id))
                return id;

            // Live and deleted alike, in the day's order: delete the morning Snack and the sheet's first Snack row is the
            // deleted one, not the afternoon's.
            List<KnownOrdered> named = [.. _known.Slots.Where(s => Names.Same(s.Name, name))
                .OrderBy(s => s.Position).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id)];
            if (nth < named.Count && !named[nth].Deleted)
                id = named[nth].Id;
            else if (nth < named.Count)
            {
                leftOut.Add($"\"{label}\": that slot was deleted here, so its row is left out of the plan.");
                id = null;
            }
            else
            {
                id = newId();
                decimal position = ListPosition.AtEnd(positions);
                positions.Add(position);
                edits.Add(new(SlotRules.Entity, id.Value, OpKind.Create,
                [
                    new OpField(SlotRules.NameField, name),
                    new OpField(SlotRules.PositionField, ListPosition.Format(position)),
                ]));
                newSlots++;
            }
            if (id is not null && !Names.Same(label, name) && saidLabel.Add(label))
                leftOut.Add($"\"{label}\" is read as the slot {name}, for everyone - the rest of the label is not imported.");
            return slotIds[at] = id;
        }

        List<PlanWeek> weeks = [];
        foreach (PlanWeek week in _sheet.Plan)
        {
            if (week.Number < 1 || week.Number > PlanTemplateRules.MaxWeeks)
                leftOut.Add($"Week {week.Number}: a plan has weeks 1 to {PlanTemplateRules.MaxWeeks}, so it is left out.");
            else if (weeks.Any(w => w.Number == week.Number))
                leftOut.Add($"Week {week.Number}: in the sheet twice - the first is imported.");
            else
                weeks.Add(week);
        }
        if (weeks.Count == 0)
            return none;

        int weekCount = weeks.Max(w => w.Number);
        edits.Add(new(PlanTemplateRules.Entity, PlanTemplateId, OpKind.Create,
        [
            new OpField(PlanTemplateRules.NameField, PlanTemplateName),
            new OpField(PlanTemplateRules.WeekCountField, OpValues.FormatInt(weekCount)),
        ]));

        int entries = 0;
        Dictionary<string, List<SheetPlace?>> twice = new(StringComparer.Ordinal);
        foreach (PlanWeek week in weeks)
        {
            Dictionary<string, int> seenSlots = new(StringComparer.OrdinalIgnoreCase);
            foreach (PlanRow row in week.Rows)
            {
                int nth = seenSlots[row.Slot] = seenSlots.GetValueOrDefault(row.Slot, -1) + 1;
                if (row.Days.All(d => d is null))
                    continue;
                if (SlotFor(row.Label, row.Slot, nth) is not { } slot)
                    continue;

                for (int i = 0; i < row.Days.Count && i < week.Days.Count; i++)
                {
                    if (row.Days[i] is not { } cell || PlanDays.Read(week.Days[i]) is not { } day)
                        continue;
                    if (CellNames(cell, known) is not { } cellChoices)
                    {
                        leftOut.Add($"{cell.Place} \"{cell.Original}\": a choice between several things eaten together - add it on the plan's page.");
                        continue;
                    }
                    if (cellChoices.Count == 0)
                        continue;

                    // One choice of several things: an entry each ("Rice cakes, cheese, apple"). Several choices: one entry.
                    IEnumerable<List<string>> perEntry = cellChoices.Count == 1
                        ? cellChoices[0].Select(n => new List<string> { n })
                        : [[.. cellChoices.Select(c => c[0])]];
                    HashSet<Guid> inCell = [];
                    bool repeated = false;
                    foreach (List<string> names in perEntry)
                    {
                        List<Guid> mealIds = [];
                        foreach (string name in names)
                            if (MealFor(name, cell.Place, ref newLines) is { } meal)
                            {
                                if (inCell.Add(meal))
                                    mealIds.Add(meal);
                                else
                                    repeated = true;
                            }
                        if (mealIds.Count == 0)
                            continue;

                        Guid entry = newId();
                        edits.Add(new(TemplateEntryRules.Entity, entry, OpKind.Create,
                        [
                            new OpField(TemplateEntryRules.TemplateField, OpValues.FormatId(PlanTemplateId)),
                            new OpField(TemplateEntryRules.WeekField, OpValues.FormatInt(week.Number)),
                            new OpField(TemplateEntryRules.DayField, PlanDays.Format(day)),
                            new OpField(TemplateEntryRules.SlotField, OpValues.FormatId(slot)),
                        ]));
                        foreach (Guid meal in mealIds)
                            edits.Add(new(TemplateChoiceRules.Entity, newId(), OpKind.Create,
                            [
                                new OpField(TemplateChoiceRules.EntryField, OpValues.FormatId(entry)),
                                new OpField(TemplateChoiceRules.MealField, OpValues.FormatId(meal)),
                            ]));
                        entries++;
                    }
                    if (repeated)
                    {
                        if (!twice.TryGetValue(cell.Original, out List<SheetPlace?>? places))
                            twice[cell.Original] = places = [];
                        places.Add(cell.Place);
                    }
                }
            }
        }
        foreach ((string text, List<SheetPlace?> places) in twice)
            leftOut.Add($"\"{text}\" names the same meal twice - planned once ({string.Join(", ", places)}).");

        newMeals += createdMeals;
        return new PlanImportCounts(weekCount, entries, newSlots, createdMeals);
    }
}
