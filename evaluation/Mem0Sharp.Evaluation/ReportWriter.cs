using System.Text;
using System.Text.Json;

namespace Mem0Sharp.Evaluation;

internal static class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static string Write(EvaluationReport report, string resultsDirectory)
    {
        Directory.CreateDirectory(resultsDirectory);
        var stamp = report.Timestamp.ToString("yyyyMMdd-HHmmss");
        var jsonPath = Path.Combine(resultsDirectory, $"evaluation-{stamp}.json");
        var markdownPath = Path.Combine(resultsDirectory, $"evaluation-{stamp}.md");

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(markdownPath, ToMarkdown(report));
        return markdownPath;
    }

    internal static string ToMarkdown(EvaluationReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Mem0Sharp evaluation results");
        builder.AppendLine();
        builder.AppendLine($"- Date: {report.Timestamp:yyyy-MM-dd HH:mm} UTC");
        builder.AppendLine($"- Mode: {report.Mode}");
        if (report.ChatModel is not null) builder.AppendLine($"- Chat model: {report.ChatModel}");
        if (report.EmbeddingModel is not null) builder.AppendLine($"- Embedding model: {report.EmbeddingModel}");
        if (report.JudgeModel is not null) builder.AppendLine($"- Judge model: {report.JudgeModel}");
        builder.AppendLine($"- Store: {report.Store}");
        builder.AppendLine();

        builder.AppendLine("## Scenario summary");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Accuracy (J) | Mean F1 | Mean BLEU-1 | Retrieval hit rate | Memories | Mean search (ms) | Ingest (s) |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var scenario in report.ScenarioReports)
        {
            var accuracy = scenario.Accuracy is null ? "n/a" : $"{scenario.Accuracy.Value:P0} ({scenario.Correct}/{scenario.Judged})";
            var f1 = scenario.MeanF1 is null ? "n/a" : $"{scenario.MeanF1.Value:F2}";
            var bleu = scenario.MeanBleu1 is null ? "n/a" : $"{scenario.MeanBleu1.Value:F2}";
            builder.AppendLine($"| {scenario.Name} | {accuracy} | {f1} | {bleu} | {scenario.RetrievalHitRate:P0} | {scenario.MemoriesStored} | {scenario.MeanSearchLatencyMs:F0} | {scenario.IngestSeconds:F1} |");
        }
        builder.AppendLine();

        builder.AppendLine("## Accuracy by category");
        builder.AppendLine();
        builder.Append("| Scenario");
        foreach (var category in EvaluationDataset.Categories) builder.Append($" | {category}");
        builder.AppendLine();
        builder.Append("| ---");
        foreach (var _ in EvaluationDataset.Categories) builder.Append(" | ---");
        builder.AppendLine();
        foreach (var scenario in report.ScenarioReports)
        {
            builder.Append($"| {scenario.Name}");
            foreach (var category in scenario.Categories)
            {
                builder.Append(category.Questions == 0 ? " | n/a" : $" | {category.Accuracy:P0}");
            }
            builder.AppendLine();
        }
        builder.AppendLine();

        foreach (var scenario in report.ScenarioReports)
        {
            builder.AppendLine($"## Scenario: {scenario.Name}");
            builder.AppendLine();
            builder.AppendLine(scenario.Description);
            builder.AppendLine();
            if (scenario.Error is not null)
            {
                builder.AppendLine($"**Failed:** {scenario.Error}");
                builder.AppendLine();
                continue;
            }
            builder.AppendLine("| Question | Category | Judgment | F1 | Generated answer |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var result in scenario.Results)
            {
                var judgment = result.Correct is null ? (result.RetrievalHit ? "hit" : "miss") : (result.Correct.Value ? "CORRECT" : "WRONG");
                var f1 = result.F1 is null ? "-" : result.F1.Value.ToString("F2");
                var answer = (result.GeneratedAnswer ?? "-").Replace("|", "\\|").Replace("\n", " ");
                builder.AppendLine($"| {result.QuestionId} | {result.Category} | {judgment} | {f1} | {answer} |");
            }
            builder.AppendLine();
        }

        return builder.ToString();
    }
}
