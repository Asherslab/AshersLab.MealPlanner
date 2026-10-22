namespace AshersLab.MealPlanner.Shared.Domain.Planning;

/// <summary>What a calendar entry is to the serves: it cooks a batch, eats leftovers from one, or neither.</summary>
public enum ServesRole
{
    Other,
    Cooked,
    Leftovers,
}

/// <summary>
/// One calendar entry as the serves read it, where it is now.
/// </summary>
/// <param name="SlotPosition">Its slot's position: what orders two entries on one day (lunch before dinner).</param>
/// <param name="Eaters">
/// The serves it eats - each eater's portion added up, the people chosen or the whole household for everyone
/// (<see cref="Serves.PortionsOf"/>). Two adults and a toddler on half a serve eat 2.5.
/// </param>
/// <param name="Made">A cooked entry's serves: the planner's number, or the meal's serves per batch.</param>
/// <param name="EatsFrom">A leftovers entry's chosen batch (the cooked entry's id); null eats whatever is left, oldest first.</param>
public sealed record ServesEntry(
    Guid       Id,
    DateOnly   Day,
    decimal    SlotPosition,
    ServesRole Role,
    bool       Skipped,
    decimal    Eaters,
    int        Made = 0,
    Guid?      EatsFrom = null
);

/// <summary>Why a leftovers entry has less than it needs.</summary>
public enum LeftoversProblem
{
    None,

    /// <summary>Every batch it could eat is eaten by then, or there is none: "nothing to eat".</summary>
    NothingLeft,

    /// <summary>Some, not all: "only 1 of 2 serves".</summary>
    NotEnough,

    /// <summary>The batch it eats from was skipped - never cooked.</summary>
    NotCooked,

    /// <summary>The batch it was set to eat from is not on the calendar any more (removed, or no longer a cooked meal).</summary>
    BatchGone,

    /// <summary>The batch it was set to eat from is cooked after it.</summary>
    CookedLater,
}

/// <summary>Serves taken from a batch: by its own meal, or by a leftovers entry, and what the batch has after.</summary>
public sealed record ServesDraw(Guid EntryId, DateOnly Day, decimal Serves, decimal LeftAfter);

/// <summary>A cooked entry's batch: what it made, who ate from it in order, and what is left at the end.</summary>
/// <param name="Cooked">False when the entry was skipped: nothing was made.</param>
/// <param name="Short">Serves its own meal needed beyond what it made: "makes 2, 3 eat it".</param>
public sealed record BatchServes(Guid Id, DateOnly Day, bool Cooked, int Made, decimal Short, IReadOnlyList<ServesDraw> Draws)
{
    /// <summary>Left after everything planned has eaten from it: more than nothing "will go uneaten".</summary>
    public decimal Left => Cooked ? Made - Draws.Sum(d => d.Serves) : 0;

    /// <summary>The draws by leftovers entries, its own meal's left out.</summary>
    public IEnumerable<ServesDraw> Leftovers => Draws.Where(d => d.EntryId != Id);
}

/// <summary>A leftovers entry: the batch it eats (or would have), what it needs and what it gets.</summary>
public sealed record LeftoversServes(Guid Id, Guid? BatchId, decimal Need, decimal Got, LeftoversProblem Problem)
{
    /// <summary>What the batch has left once this has eaten; null with no batch.</summary>
    public decimal? LeftAfter { get; init; }
}

/// <summary>Every batch and every leftovers entry in the entries given.</summary>
public sealed record ServesLedger(IReadOnlyDictionary<Guid, BatchServes> Batches, IReadOnlyDictionary<Guid, LeftoversServes> Leftovers);

/// <summary>
/// <b>Serves are counted, never stored.</b> A batch is a cooked calendar entry; what is left of it is what it made less a
/// serve per eater of every entry that eats from it - its own meal first, then each leftovers entry in calendar order. So
/// two phones adding a leftovers lunch each, offline, are two entries and two draws: nothing to merge, nothing lost, no
/// counter op. The planner's own numbers - the serves made, which batch a lunch eats - are plain fields, and the later
/// edit of each wins.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A skipped cooked entry made nothing. A leftovers entry set to eat from it is flagged
///   <see cref="LeftoversProblem.NotCooked"/> - never left pointing at serves that do not exist - and one eating whatever is
///   left looks for another batch, flagged the same way when the skipped one was all there was.</item>
/// <item>A skipped leftovers entry eats nothing: its serves stay in the batch for whoever is next.</item>
/// <item>"Whatever is left" is the oldest batch cooked in the <see cref="KeepsDays"/> days before it that still has enough,
///   else the oldest with any. Oldest first: the batch that would spoil first is eaten first.</item>
/// <item>Order is the calendar's: the day, then the slot's position - so lunch eats Monday's dinner only from Tuesday.</item>
/// </list>
/// </remarks>
public static class Serves
{
    /// <summary>Leftovers eat batches cooked at most this many days before them, unless one is chosen.</summary>
    public const int KeepsDays = 7;

    /// <summary>
    /// The serves an entry eats: each eater's portion (a full serve unless set; a toddler's half), added up - the people
    /// chosen, or for everyone the whole household. Someone chosen who is no longer in the household eats a full serve;
    /// a household with nobody added yet still eats one.
    /// </summary>
    /// <param name="household">Each live person's portion, by id.</param>
    public static decimal PortionsOf(IReadOnlyList<Guid>? people, IReadOnlyDictionary<Guid, decimal> household) =>
        people is { Count: > 0 } ? people.Sum(p => household.GetValueOrDefault(p, 1m))
        : household.Count > 0 ? household.Values.Sum()
        : 1m;

    /// <summary>"2", "2½", "1¼", "¾" - serves as the planner reads them, in quarters.</summary>
    public static string Format(decimal serves)
    {
        decimal whole = decimal.Truncate(serves);
        string? fraction = ((serves - whole) * 4) switch { 0 => "", 1 => "¼", 2 => "½", 3 => "¾", _ => null };
        return fraction is null ? serves.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : whole == 0 && fraction.Length > 0 ? fraction
            : whole.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + fraction;
    }

    public static ServesLedger Count(IEnumerable<ServesEntry> entries)
    {
        List<ServesEntry> ordered = [.. entries.OrderBy(e => e.Day).ThenBy(e => e.SlotPosition).ThenBy(e => e.Id)];
        HashSet<Guid> cooked = [.. ordered.Where(e => e.Role == ServesRole.Cooked).Select(e => e.Id)];

        // Batches so far, in calendar order: a batch not in here yet is cooked later than the entry being counted.
        List<Batch> batches = [];
        Dictionary<Guid, LeftoversServes> leftovers = [];

        foreach (ServesEntry entry in ordered)
        {
            switch (entry)
            {
                case { Role: ServesRole.Cooked }:
                {
                    Batch batch = new(entry);
                    if (!entry.Skipped)
                    {
                        decimal own = Math.Min(entry.Eaters, entry.Made);
                        batch.Left  = entry.Made - own;
                        batch.Short = entry.Eaters - own;
                        batch.Draws.Add(new ServesDraw(entry.Id, entry.Day, own, batch.Left));
                    }
                    batches.Add(batch);
                    break;
                }

                case { Role: ServesRole.Leftovers, Skipped: false }:
                    leftovers[entry.Id] = Eat(entry, cooked, batches);
                    break;
            }
        }

        return new ServesLedger(
            batches.ToDictionary(b => b.Entry.Id, b => new BatchServes(b.Entry.Id, b.Entry.Day, !b.Entry.Skipped,
                b.Entry.Skipped ? 0 : b.Entry.Made, b.Short, b.Draws)),
            leftovers);
    }

    private sealed class Batch(ServesEntry entry)
    {
        public ServesEntry      Entry { get; } = entry;
        public List<ServesDraw> Draws { get; } = [];
        public decimal          Left  { get; set; }
        public decimal          Short { get; set; }
    }

    private static LeftoversServes Eat(ServesEntry entry, HashSet<Guid> cooked, List<Batch> batches)
    {
        decimal need = entry.Eaters;

        if (entry.EatsFrom is { } chosen)
        {
            if (batches.Find(b => b.Entry.Id == chosen) is not { } batch)
                return new LeftoversServes(entry.Id, chosen, need, 0, cooked.Contains(chosen) ? LeftoversProblem.CookedLater : LeftoversProblem.BatchGone);
            return batch.Entry.Skipped
                ? new LeftoversServes(entry.Id, chosen, need, 0, LeftoversProblem.NotCooked)
                : Draw(entry, batch);
        }

        DateOnly oldest = entry.Day.AddDays(-KeepsDays);
        List<Batch> fresh = [.. batches.Where(b => b.Entry.Day >= oldest)];
        if ((fresh.Find(b => !b.Entry.Skipped && b.Left >= need) ?? fresh.Find(b => !b.Entry.Skipped && b.Left > 0)) is { } pick)
            return Draw(entry, pick);

        // Nothing to eat. When a batch it could have eaten was skipped, that is why.
        return fresh.FindLast(b => b.Entry.Skipped) is { } skipped
            ? new LeftoversServes(entry.Id, skipped.Entry.Id, need, 0, LeftoversProblem.NotCooked)
            : new LeftoversServes(entry.Id, null, need, 0, LeftoversProblem.NothingLeft);
    }

    private static LeftoversServes Draw(ServesEntry entry, Batch batch)
    {
        decimal need = entry.Eaters;
        decimal got  = Math.Min(need, batch.Left);
        batch.Left -= got;
        if (got > 0)
            batch.Draws.Add(new ServesDraw(entry.Id, entry.Day, got, batch.Left));

        LeftoversProblem problem = got == need ? LeftoversProblem.None : got == 0 ? LeftoversProblem.NothingLeft : LeftoversProblem.NotEnough;
        return new LeftoversServes(entry.Id, batch.Entry.Id, need, got, problem) { LeftAfter = batch.Left };
    }
}
