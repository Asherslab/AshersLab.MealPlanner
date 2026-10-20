using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Domain;

public class HlcTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    [Fact]
    public void First_event_takes_the_wall_clock()
    {
        Assert.Equal(new Hlc(1_000, 0, A), Hlc.Next(null, 1_000, A));
    }

    [Fact]
    public void Events_in_the_same_millisecond_count_up()
    {
        Hlc first  = Hlc.Next(null, 1_000, A);
        Hlc second = Hlc.Next(first, 1_000, A);

        Assert.Equal(new Hlc(1_000, 1, A), second);
        Assert.True(second > first);
    }

    [Fact]
    public void A_wall_clock_that_went_backwards_never_orders_an_event_earlier()
    {
        Hlc first  = Hlc.Next(null, 5_000, A);
        Hlc second = Hlc.Next(first, 4_000, A);

        Assert.True(second > first);
        Assert.Equal(5_000, second.WallMs);
    }

    [Fact]
    public void An_edit_made_after_seeing_a_fast_clock_orders_after_it()
    {
        // B's clock is five minutes ahead. A sees B's op, then edits: A's edit must win, though A's wall clock
        // says it is earlier.
        Hlc fromB = new(1_000 + 300_000, 3, B);
        Hlc stateA = Hlc.Observe(Hlc.Next(null, 1_000, A), fromB, A);
        Hlc editA  = Hlc.Next(stateA, 2_000, A);

        Assert.True(editA > fromB);
        Assert.Equal(A, editA.DeviceId);
    }

    [Fact]
    public void Observing_an_older_clock_changes_nothing()
    {
        Hlc mine = new(9_000, 2, A);

        Assert.Equal(mine, Hlc.Observe(mine, new Hlc(8_000, 7, B), A));
    }

    [Fact]
    public void The_counter_rolls_into_the_next_millisecond_rather_than_overflow()
    {
        Hlc full = new(1_000, Hlc.MaxCounter, A);

        Assert.Equal(new Hlc(1_001, 0, A), Hlc.Next(full, 1_000, A));
    }

    [Fact]
    public void Same_time_breaks_the_tie_by_device()
    {
        Assert.True(new Hlc(1_000, 0, B) > new Hlc(1_000, 0, A));
    }

    [Theory]
    [InlineData(1_000, 0, 1_000, 1)]
    [InlineData(1_000, 99, 1_001, 0)]
    [InlineData(999, 5, 1_000, 0)]
    [InlineData(9_999_999_999_999, 0, 10_000_000_000_000, 0)]
    public void The_string_form_sorts_like_the_clock(long wallA, int counterA, long wallB, int counterB)
    {
        Hlc earlier = new(wallA, counterA, B);
        Hlc later   = new(wallB, counterB, A);

        Assert.True(later > earlier);
        Assert.True(string.CompareOrdinal(later.ToString(), earlier.ToString()) > 0);
    }

    [Fact]
    public void Device_tie_break_sorts_the_same_as_the_string_form()
    {
        // Guid.CompareTo does not order by the hex text; the clock must, or SQL ORDER BY and the merge disagree.
        Guid low  = Guid.Parse("0f000000-0000-0000-0000-000000000000");
        Guid high = Guid.Parse("00000000-0000-0000-0000-0000000000f0");
        Hlc  x    = new(1, 0, low);
        Hlc  y    = new(1, 0, high);

        Assert.Equal(Math.Sign(string.CompareOrdinal(x.ToString(), y.ToString())), Math.Sign(x.CompareTo(y)));
    }

    [Fact]
    public void Round_trips_through_its_string_form()
    {
        Hlc clock = new(1_792_000_000_123, 42, Guid.CreateVersion7());

        Assert.Equal(clock, Hlc.Parse(clock.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a clock")]
    [InlineData("000001792000000123.00042.zz000000000000000000000000000000")]
    [InlineData("000001792000000123-00042-00000000000000000000000000000000")]
    public void Refuses_what_is_not_a_clock(string value)
    {
        Assert.False(Hlc.TryParse(value, out _));
    }
}
