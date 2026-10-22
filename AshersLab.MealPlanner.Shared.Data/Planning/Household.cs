using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Shared.Data.Planning;

/// <summary>The household as the serves count it: each live person's portion, a full serve unless set.</summary>
public static class Household
{
    /// <summary>Each live person's portion by id, for <c>Serves.PortionsOf</c>.</summary>
    public static async Task<IReadOnlyDictionary<Guid, decimal>> PortionsAsync(DbContext db, CancellationToken cancellationToken = default) =>
        (await db.Set<Person>().AsNoTracking().Where(p => !p.Deleted).Select(p => new { p.Id, p.Portion }).ToListAsync(cancellationToken))
        .ToDictionary(p => p.Id, p => p.Portion ?? 1m);
}
