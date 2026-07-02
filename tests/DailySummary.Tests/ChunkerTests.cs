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
        // step = 80: windows [0,100) [80,180) [160,250). The third already reaches the end
        // (160+90 == 250), so the chunker stops — a fourth [240,250) window would be redundant
        // (fully inside the third). 3 chunks cover all 250 chars with 20-char overlap.
        Assert.Equal(3, chunks.Count);
    }

    [Fact]
    public void EmptyText_IsNoChunks()
    {
        Assert.Empty(TextChunker.Split(""));
    }
}
