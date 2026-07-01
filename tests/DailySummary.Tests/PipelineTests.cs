using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;
using DailySummary.Core.Pipeline;
using DailySummary.Core.Summarization;
using Xunit;

namespace DailySummary.Tests;

public class PipelineTests
{
    private static GatherSummarizePipeline Build(ISectionGatherer gatherer)
    {
        var gatherers = new SectionGathererRegistry(new[] { gatherer });
        var summarizers = new SummarizerRegistry(new ISummarizer[] { new FakeSummarizer() });
        var sectionSummarizers = new SectionSummarizers(summarizers);
        return new GatherSummarizePipeline(gatherers, sectionSummarizers, summarizers, new ChunkedSummarizer());
    }

    private static (AppConfig app, DigestConfig digest) Config()
    {
        var app = new AppConfig { Concurrency = new() { Fetch = 4, Summarize = 1 }, ChannelCapacity = 8 };
        var digest = new DigestConfig
        {
            Name = "t",
            Sections =
            {
                new SectionConfig { Type = SectionType.Web, Heading = "Web", Order = 0, Summarizer = "fake", TimeoutSeconds = 5 }
            }
        };
        return (app, digest);
    }

    [Fact]
    public async Task Summarizes_Gathered_Pieces()
    {
        var pieces = new[]
        {
            new RawPiece(0, "Web", null, null, "alpha"),
            new RawPiece(0, "Web", null, null, "beta")
        };
        var pipeline = Build(new FakeGatherer(SectionType.Web, pieces));
        var (app, digest) = Config();

        var result = await pipeline.RunAsync(digest, app, default);

        var section = Assert.Single(result.Sections);
        Assert.Equal("Web", section.Heading);
        var body = Assert.Single(section.Entries).Body;
        Assert.Contains("alpha", body); // summarized (and folded) content survives
        Assert.Contains("beta", body);
    }

    [Fact]
    public async Task Failed_Piece_Renders_Unavailable()
    {
        var pieces = new[] { new RawPiece(0, "Web", null, null, "", Error: "boom") };
        var pipeline = Build(new FakeGatherer(SectionType.Web, pieces));
        var (app, digest) = Config();

        var result = await pipeline.RunAsync(digest, app, default);

        var body = result.Sections.Single().Entries.Single().Body;
        Assert.Contains("unable to complete", body);
    }

    [Fact]
    public async Task Gatherer_Throwing_Does_Not_Abort_Run()
    {
        var pipeline = Build(new FakeGatherer(SectionType.Web, Array.Empty<RawPiece>(), throws: true));
        var (app, digest) = Config();

        var result = await pipeline.RunAsync(digest, app, default);

        var body = result.Sections.Single().Entries.Single().Body;
        Assert.Contains("unable to complete", body);
    }
}
