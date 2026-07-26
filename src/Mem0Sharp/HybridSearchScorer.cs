namespace Mem0Sharp;

internal static class HybridSearchScorer
{
    public static IReadOnlyList<SearchResult> ScoreAndRank(string query, IReadOnlyList<SearchResult> semanticResults, IReadOnlyDictionary<string, double> entityBoosts, double threshold, int topK, bool explain)
    {
        var eligible = semanticResults.Where(result => result.Score >= threshold).ToArray();
        if (eligible.Length == 0) return [];

        var queryTerms = Tokenize(query);
        var keywordScores = queryTerms.Length == 0 ? new Dictionary<string, double>() : ScoreBm25(queryTerms, eligible);
        var hasKeyword = keywordScores.Count > 0;
        var hasEntity = entityBoosts.Count > 0;
        var maxPossible = 1d + (hasKeyword ? 1d : 0d) + (hasEntity ? 0.5d : 0d);

        return eligible.Select(result =>
            {
                var keyword = keywordScores.GetValueOrDefault(result.Memory.Id);
                var entity = entityBoosts.GetValueOrDefault(result.Memory.Id);
                var raw = result.Score + keyword + entity;
                var score = Math.Min(raw / maxPossible, 1);
                var details = explain
                    ? new SearchScoreDetails(result.Score, keyword, entity, Raw: raw, MaxPossible: maxPossible, Threshold: threshold)
                    : null;
                return new SearchResult(result.Memory, score, details);
            })
            .OrderByDescending(result => result.Score)
            .Take(topK)
            .ToArray();
    }

    private static Dictionary<string, double> ScoreBm25(IReadOnlyList<string> queryTerms, IReadOnlyList<SearchResult> candidates)
    {
        const double k1 = 1.5;
        const double b = 0.75;
        var documents = candidates.Select(result => Tokenize(result.Memory.Text)).ToArray();
        var averageLength = documents.Average(document => Math.Max(document.Length, 1));
        var rawScores = new Dictionary<string, double>(StringComparer.Ordinal);

        for (var documentIndex = 0; documentIndex < documents.Length; documentIndex++)
        {
            var document = documents[documentIndex];
            var frequencies = document.GroupBy(term => term).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            double score = 0;
            foreach (var term in queryTerms.Distinct(StringComparer.Ordinal))
            {
                var documentFrequency = documents.Count(item => item.Contains(term, StringComparer.Ordinal));
                if (documentFrequency == 0 || !frequencies.TryGetValue(term, out var frequency)) continue;
                var inverseDocumentFrequency = Math.Log(1 + (documents.Length - documentFrequency + 0.5) / (documentFrequency + 0.5));
                score += inverseDocumentFrequency * frequency * (k1 + 1) /
                    (frequency + k1 * (1 - b + b * document.Length / averageLength));
            }
            if (score > 0) rawScores[candidates[documentIndex].Memory.Id] = NormalizeBm25(score, queryTerms.Count);
        }
        return rawScores;
    }

    private static double NormalizeBm25(double score, int queryTermCount)
    {
        var (midpoint, steepness) = queryTermCount switch
        {
            <= 3 => (5d, 0.7d),
            <= 6 => (7d, 0.6d),
            <= 9 => (9d, 0.5d),
            <= 15 => (10d, 0.5d),
            _ => (12d, 0.5d)
        };
        return 1 / (1 + Math.Exp(-steepness * (score - midpoint)));
    }

    private static string[] Tokenize(string text) => new string(text.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : ' ').ToArray())
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}