using System.Globalization;

namespace AshersLab.MealPlanner.Shared.Domain.Sync;

/// <summary>
/// A hybrid logical clock reading: wall time in milliseconds, a counter for events within (or behind) the
/// same millisecond, and the device that made the event. Orders every op in the system - the higher one wins
/// a same-field conflict (docs/modules/sync/offline-sync.md).
/// </summary>
/// <remarks>
/// Why not a plain timestamp: a phone whose clock is a few minutes slow would lose every conflict to an edit
/// it had already seen. <see cref="Observe"/> moves this device's clock past anything it has received, so an
/// edit made after seeing another always orders after it, whatever the wall clocks say.
/// <para>
/// Two readings from different devices are never equal: the device id breaks the tie. Stored and sent as
/// <see cref="ToString"/>, whose ordinal order is the same as <see cref="CompareTo"/> - so SQL can order by it.
/// </para>
/// </remarks>
public readonly record struct Hlc(long WallMs, int Counter, Guid DeviceId) : IComparable<Hlc>
{
    /// <summary>Five decimal digits in the string form.</summary>
    public const int MaxCounter = 99_999;

    /// <summary>
    /// The clock for a new event on <paramref name="device"/>: later than <paramref name="last"/> (this
    /// device's clock state, null on its first event) and never behind the wall clock.
    /// </summary>
    public static Hlc Next(Hlc? last, long nowMs, Guid device)
    {
        if (last is not { } l || nowMs > l.WallMs)
            return new Hlc(nowMs, 0, device);

        // The wall clock has not moved past the last event (same millisecond, or this device has seen an op
        // from a device whose clock runs ahead): keep the larger wall time and count.
        if (l.Counter >= MaxCounter)
            return new Hlc(l.WallMs + 1, 0, device);

        return new Hlc(l.WallMs, l.Counter + 1, device);
    }

    /// <summary>
    /// This device's clock state after receiving <paramref name="remote"/>: the later of the two, kept as this
    /// device's. Not itself an event - the next <see cref="Next"/> orders after both.
    /// </summary>
    public static Hlc Observe(Hlc? last, Hlc remote, Guid device)
    {
        if (last is not { } l)
            return remote with { DeviceId = device };

        return (remote.WallMs, remote.Counter).CompareTo((l.WallMs, l.Counter)) > 0
            ? remote with { DeviceId = device }
            : l;
    }

    public int CompareTo(Hlc other)
    {
        int byTime = (WallMs, Counter).CompareTo((other.WallMs, other.Counter));

        return byTime != 0
            ? byTime
            : string.CompareOrdinal(DeviceId.ToString("N"), other.DeviceId.ToString("N"));
    }

    public static bool operator >(Hlc a, Hlc b) => a.CompareTo(b) > 0;
    public static bool operator <(Hlc a, Hlc b) => a.CompareTo(b) < 0;
    public static bool operator >=(Hlc a, Hlc b) => a.CompareTo(b) >= 0;
    public static bool operator <=(Hlc a, Hlc b) => a.CompareTo(b) <= 0;

    /// <summary><c>000001792000000000.00000.&lt;device, 32 hex&gt;</c> - fixed width, so ordinal order is clock order.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{WallMs:D18}.{Counter:D5}.{DeviceId:N}");

    public static Hlc Parse(string value)
    {
        if (!TryParse(value, out Hlc hlc))
            throw new FormatException($"Not a clock reading: '{value}'.");

        return hlc;
    }

    public static bool TryParse(string? value, out Hlc hlc)
    {
        hlc = default;

        if (value is not { Length: 18 + 1 + 5 + 1 + 32 } || value[18] != '.' || value[24] != '.')
            return false;

        if (!long.TryParse(value.AsSpan(0, 18), NumberStyles.None, CultureInfo.InvariantCulture, out long wall)
         || !int.TryParse(value.AsSpan(19, 5), NumberStyles.None, CultureInfo.InvariantCulture, out int counter)
         || !Guid.TryParseExact(value.AsSpan(25), "N", out Guid device))
            return false;

        hlc = new Hlc(wall, counter, device);
        return true;
    }
}
