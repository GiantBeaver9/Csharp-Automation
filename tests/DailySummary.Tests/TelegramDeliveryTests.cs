using DailySummary.Providers.Delivery;
using Xunit;

namespace DailySummary.Tests;

public class TelegramDeliveryTests
{
    [Fact]
    public void Empty_Or_Whitespace_Text_Yields_No_Chunks()
    {
        Assert.Empty(TelegramDelivery.Chunks("", 4096));
        Assert.Empty(TelegramDelivery.Chunks("   \n\t", 4096));   // whitespace-only is not worth a blank Telegram send
    }

    [Fact]
    public void Chunks_Rejects_A_Limit_Below_Two()
    {
        // A surrogate pair is 2 UTF-16 units, so limit < 2 can't make progress — reject it up front.
        // .ToList() forces the iterator to run so the guard actually throws.
        Assert.Throws<ArgumentOutOfRangeException>(() => TelegramDelivery.Chunks("ab", 1).ToList());
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
    public void TryGetRetryAfter_Reads_The_Documented_429_Body()
    {
        // The exact shape Telegram returns on flood control.
        var body = "{\"ok\":false,\"error_code\":429,\"description\":\"Too Many Requests: retry after 35\","
                 + "\"parameters\":{\"retry_after\":35}}";
        Assert.Equal(35, TelegramDelivery.TryGetRetryAfter(body));
    }

    [Theory]
    [InlineData("{\"ok\":false,\"error_code\":400,\"description\":\"Bad Request\"}")]  // no parameters
    [InlineData("{\"parameters\":{}}")]                                               // parameters, no retry_after
    [InlineData("Bad Gateway")]                                                       // non-JSON body
    [InlineData("")]                                                                  // empty body
    public void TryGetRetryAfter_Returns_Null_When_Absent_Or_Unparseable(string body)
    {
        Assert.Null(TelegramDelivery.TryGetRetryAfter(body));
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
