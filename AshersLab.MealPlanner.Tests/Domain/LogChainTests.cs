using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Domain;

public class LogChainTests
{
    private static readonly Guid First  = Guid.Parse("0199f0aa-0000-7000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("0199f0aa-0000-7000-8000-000000000002");

    [Fact]
    public void Is_the_same_for_the_same_ops_in_the_same_order()
    {
        Assert.Equal(
            LogChain.Next(LogChain.Next(LogChain.Empty, First), Second),
            LogChain.Next(LogChain.Next(LogChain.Empty, First), Second));
    }

    [Fact]
    public void Differs_when_any_earlier_op_differs()
    {
        // The restore case: seq 2 is the same op, but seq 1 is not - the chain at 2 must say so.
        string original = LogChain.Next(LogChain.Next(LogChain.Empty, First), Second);
        string renumbered = LogChain.Next(LogChain.Next(LogChain.Empty, Guid.CreateVersion7()), Second);

        Assert.NotEqual(original, renumbered);
        Assert.NotEqual(LogChain.Next(LogChain.Empty, First), LogChain.Next(LogChain.Empty, Second));
    }

    [Fact]
    public void Is_32_lowercase_hex_characters()
    {
        string chain = LogChain.Next(LogChain.Empty, First);

        Assert.Equal(32, chain.Length);
        Assert.All(chain, c => Assert.True(char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c)));
    }
}
