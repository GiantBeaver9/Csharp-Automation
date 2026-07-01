using DailySummary.Core.Summarization;
using Xunit;

namespace DailySummary.Tests;

public class ChunkerTests
{
    [Fact]
    public void ShortText_IsOneChunk()
    {
        var chunks = TextChunker.Split("hello world", maxChars: 100, overlap: 10);
        Assert.Single(chunks);
    }

    [Fact]
    public void LongText_SplitsWithOverlap()
    {
        var text = new string('a', 250);
        var chunks = TextChunker.Split(text, maxChars: 100, overlap: 20);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= 100));
        // step = 80, so windows start at 0, 80, 160, 240 -> 4 chunks covering all 250 chars.
        Assert.Equal(4, chunks.Count);
    }

    [Fact]
    public void EmptyText_IsNoChunks()
    {
        Assert.Empty(TextChunker.Split(""));
    }
}
