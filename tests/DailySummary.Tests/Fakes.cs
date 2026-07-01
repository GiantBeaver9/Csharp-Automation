using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Tests;

internal sealed class FakeGatherer : ISectionGatherer
{
    private readonly IReadOnlyList<RawPiece> _pieces;
    private readonly bool _throws;

    public FakeGatherer(SectionType type, IReadOnlyList<RawPiece> pieces, bool throws = false)
    {
        Type = type;
        _pieces = pieces;
        _throws = throws;
    }

    public SectionType Type { get; }

    public Task<IReadOnlyList<RawPiece>> GatherAsync(SectionConfig config, CancellationToken ct)
    {
        if (_throws) throw new InvalidOperationException("gather boom");
        return Task.FromResult(_pieces);
    }
}

/// <summary>Echoes its input so summaries are traceable in assertions.</summary>
internal sealed class FakeSummarizer : ISummarizer
{
    public string Name => "fake";

    public Task<string> SummarizeAsync(string prompt, string input, CancellationToken ct) =>
        Task.FromResult($"S({input})");
}
