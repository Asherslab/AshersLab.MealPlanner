using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Shared.Data.Planning;

/// <summary>
/// The plans as the pages read them, from either database in one read per table: live templates, their live entries
/// and each entry's live choices. An entry of a deleted template, or a choice of a deleted entry, is left out here - a
/// tombstone's children are shown nowhere and plan nothing.
/// </summary>
public sealed class PlanBook
{
    private readonly Dictionary<Guid, PlanTemplate>      _templates;
    private readonly ILookup<Guid, TemplateEntry>        _entriesByTemplate;
    private readonly ILookup<Guid, TemplateChoice>       _choicesByEntry;

    private PlanBook(List<PlanTemplate> templates, List<TemplateEntry> entries, List<TemplateChoice> choices)
    {
        _templates         = templates.ToDictionary(t => t.Id);
        List<TemplateEntry> live = [.. entries.Where(e => _templates.ContainsKey(e.TemplateId))];
        HashSet<Guid> liveIds = [.. live.Select(e => e.Id)];
        // Ids are GUIDv7, minted in the order things were added: the first choice is the usual one.
        _entriesByTemplate = live.OrderBy(e => e.Id).ToLookup(e => e.TemplateId);
        _choicesByEntry    = choices.Where(c => liveIds.Contains(c.EntryId)).OrderBy(c => c.Id).ToLookup(c => c.EntryId);
    }

    public static async Task<PlanBook> LoadAsync(DbContext db, CancellationToken cancellationToken = default) =>
        new(await db.Set<PlanTemplate>().AsNoTracking().Where(t => !t.Deleted).ToListAsync(cancellationToken),
            await db.Set<TemplateEntry>().AsNoTracking().Where(e => !e.Deleted).ToListAsync(cancellationToken),
            await db.Set<TemplateChoice>().AsNoTracking().Where(c => !c.Deleted).ToListAsync(cancellationToken));

    public IEnumerable<PlanTemplate> Templates =>
        _templates.Values.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(t => t.Id);

    public PlanTemplate? Template(Guid id) => _templates.GetValueOrDefault(id);

    /// <summary>Every live entry of the template, weeks beyond its count included.</summary>
    public IEnumerable<TemplateEntry> EntriesOf(Guid template) => _entriesByTemplate[template];

    public IReadOnlyList<TemplateChoice> ChoicesOf(Guid entry) => [.. _choicesByEntry[entry]];

    /// <summary>The names of the plans that use a meal - the reason it cannot be deleted.</summary>
    public IReadOnlyList<string> PlansWithMeal(Guid meal) => PlansWhere(e => ChoicesOf(e.Id).Any(c => c.MealId == meal));

    public IReadOnlyList<string> PlansWithSlot(Guid slot) => PlansWhere(e => e.SlotId == slot);

    public IReadOnlyList<string> PlansWithPerson(Guid person) => PlansWhere(e => e.PeopleIds()?.Contains(person) == true);

    private List<string> PlansWhere(Func<TemplateEntry, bool> uses) =>
        [.. Templates.Where(t => EntriesOf(t.Id).Any(uses)).Select(t => t.Name)];
}
