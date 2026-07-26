using System.Globalization;
using System.Text.RegularExpressions;

namespace Mem0Sharp;

public sealed partial class LlmReranker : IMemoryReranker
{
    private const int MaxInputLength = 4000;
    private readonly IChatCompletionClient client;

    public LlmReranker(IChatCompletionClient client) => this.client = client;

    public async Task<IReadOnlyList<SearchResult>> RerankAsync(string query, IReadOnlyList<SearchResult> candidates, int topK, CancellationToken cancellationToken = default)
    {
        var scored = new List<SearchResult>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var response = await client.CompleteAsync(
            [
                new Message("system", "Score the relevance of the document to the query from 0.0 to 1.0. Return only the number."),
                new Message("user", $"Query: {query[..Math.Min(query.Length, MaxInputLength)]}\n\nDocument: {candidate.Memory.Text[..Math.Min(candidate.Memory.Text.Length, MaxInputLength)]}")
            ], cancellationToken);
            var rerankScore = ParseScore(response);
            var details = candidate.ScoreDetails is null
                ? new SearchScoreDetails(candidate.Score, Reranker: rerankScore)
                : candidate.ScoreDetails with { Reranker = rerankScore };
            scored.Add(candidate with { Score = rerankScore, ScoreDetails = details });
        }
        return scored.OrderByDescending(result => result.Score).Take(topK).ToArray();
    }

    internal static double ParseScore(string response)
    {
        var match = ScorePattern().Match(response);
        return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var score)
            ? Math.Clamp(score, 0, 1)
            : 0.5;
    }

    [GeneratedRegex(@"-?\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex ScorePattern();
}