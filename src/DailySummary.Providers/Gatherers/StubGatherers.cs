using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Providers.Gatherers;

// These two types are scaffolded but not yet fully implemented. They return an "unavailable"
// RawPiece so the pipeline stays green (failure-isolated) until each is fleshed out.
// See SPEC.md §6 for the intended designs.

/// <summary>SQL to-do source. TODO: add a DB driver (Npgsql / Microsoft.Data.SqlClient) and run the query.</summary>
public sealed class SqlGatherer : ISectionGatherer
{
    public SectionType Type => SectionType.Sql;

    public Task<IReadOnlyList<RawPiece>> GatherAsync(SectionConfig config, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RawPiece>>(new[]
        {
            new RawPiece(config.Order, config.Heading, null, null, string.Empty,
                Error: "SqlGatherer not implemented yet (needs a DB driver + query execution).")
        });
}

/// <summary>Podcast transcription. TODO: download enclosure → ffmpeg → ITranscriber (Whisper.net).</summary>
public sealed class PodcastGatherer : ISectionGatherer
{
    private readonly ITranscriber _transcriber;

    public PodcastGatherer(ITranscriber transcriber) => _transcriber = transcriber;

    public SectionType Type => SectionType.Podcast;

    public Task<IReadOnlyList<RawPiece>> GatherAsync(SectionConfig config, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RawPiece>>(new[]
        {
            new RawPiece(config.Order, config.Heading, null, null, string.Empty,
                Error: "PodcastGatherer not implemented yet (needs feed read + ffmpeg decode + transcription).")
        });
}
