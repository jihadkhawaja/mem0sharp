using System.Text.RegularExpressions;

namespace Mem0Sharp;

public sealed partial class RuleBasedEntityExtractor : IEntityExtractor
{
    public Task<IReadOnlyList<ExtractedEntity>> ExtractAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entities = new Dictionary<string, ExtractedEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in QuotedTextPattern().Matches(text)) Add(match.Groups[1].Value, EntityType.Concept, entities);
        foreach (Match match in ProperNamePattern().Matches(text)) Add(match.Value, EntityType.Other, entities);
        return Task.FromResult<IReadOnlyList<ExtractedEntity>>(entities.Values.ToArray());
    }

    private static void Add(string text, EntityType type, IDictionary<string, ExtractedEntity> entities)
    {
        var normalized = text.Trim();
        if (normalized.Length > 1) entities.TryAdd(normalized, new ExtractedEntity(normalized, type));
    }

    [GeneratedRegex("[\\\"']([^\\\"']{2,100})[\\\"']", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedTextPattern();

    [GeneratedRegex(@"\b(?:[A-Z][\p{L}\p{M}'-]*)(?:\s+[A-Z][\p{L}\p{M}'-]*)*\b", RegexOptions.CultureInvariant)]
    private static partial Regex ProperNamePattern();
}