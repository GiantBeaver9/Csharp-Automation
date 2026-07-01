using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Core.Pipeline;

/// <summary>Dispatch by <see cref="SectionType"/> to the matching gatherer — a lookup, not a switch.</summary>
public sealed class SectionGathererRegistry
{
    private readonly IReadOnlyDictionary<SectionType, ISectionGatherer> _byType;

    public SectionGathererRegistry(IEnumerable<ISectionGatherer> gatherers) =>
        _byType = gatherers.ToDictionary(g => g.Type);

    public ISectionGatherer Resolve(SectionType type) =>
        _byType.TryGetValue(type, out var g)
            ? g
            : throw new KeyNotFoundException($"No gatherer registered for section type '{type}'.");
}
