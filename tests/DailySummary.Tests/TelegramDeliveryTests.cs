using DailySummary.Providers.Delivery;
using Xunit;

namespace DailySummary.Tests;

public class TelegramDeliveryTests
{
    [Fact]
    public void Empty_Or_Whitespace_Text_Yields_No_Chunks()
    {
        Assert.Empty(TelegramDelivery.Chunks("", 4096));
    }

    [Fact]
    public void Short_Text_Is_A_Single_Chunk()
    {
        var chunks = TelegramDelivery.Chunks("hello", 4096).ToList();
        Assert.Equal(new[] { "hello" }, chunks);
    }

    [Fact]
    public void Long_Text_Is_Split_At_The_Limit_And_Rejoins_Losslessly()
    {
        var text = new string('a', 10_000);
        var chunks = TelegramDelivery.Chunks(text, 4096).ToList();

        Assert.Equal(3, chunks.Count);                      // 4096 + 4096 + 1808
        Assert.All(chunks, c => Assert.True(c.Length <= 4096));
        Assert.Equal(text, string.Concat(chunks));          // no bytes lost or duplicated
    }

    [Fact]
    public void Never_Splits_A_Surrogate_Pair_Across_Chunks()
    {
        // A limit that lands the boundary exactly between the two halves of an emoji (2 UTF-16 units each).
        // "😀" is one surrogate pair; three of them = 6 units. With limit 3 the naive cut would fall mid-pair.
        var text = "😀😀😀";
        var chunks = TelegramDelivery.Chunks(text, 3).ToList();

        Assert.All(chunks, c => Assert.True(c.Length <= 3));
        Assert.Equal(text, string.Concat(chunks));          // reassembles exactly
        // Each chunk holds whole emoji only — no lone high/low surrogate at a boundary.
        Assert.All(chunks, c =>
        {
            Assert.False(char.IsHighSurrogate(c[^1]));       // never ends on a dangling high surrogate
            Assert.False(char.IsLowSurrogate(c[0]));         // never starts on a dangling low surrogate
        });
    }
}
