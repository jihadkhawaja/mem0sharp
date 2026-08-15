using System.Text.Json;
using Mem0Sharp;
using Mem0Sharp.Evaluation;

var scenarioFilter = new List<string>();
var selfTest = false;
var listOnly = false;
var validateDataset = false;
var configPath = Path.Combine(AppContext.BaseDirectory, "evalconfig.local.yaml");
string? datasetPath = null;

for (var index = 0; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--scenario" when index + 1 < args.Length:
            scenarioFilter.AddRange(args[++index].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            break;
        case "--config" when index + 1 < args.Length:
            configPath = args[++index];
            break;
        case "--dataset" when index + 1 < args.Length:
            datasetPath = args[++index];
            break;
        case "--self-test":
            selfTest = true;
            break;
        case "--list":
            listOnly = true;
            break;
        case "--validate-dataset":
            validateDataset = true;
            break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[index]}");
            PrintUsage();
            return 2;
    }
}

if (listOnly)
{
    Console.WriteLine("Available scenarios:");
    foreach (var scenario in Scenarios.All)
    {
        Console.WriteLine($"  {scenario.Name,-26} {scenario.Description}");
    }
    return 0;
}

var selected = scenarioFilter.Count == 0
    ? Scenarios.All
    : Scenarios.All.Where(scenario => scenarioFilter.Contains(scenario.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
if (selected.Count == 0)
{
    Console.Error.WriteLine("No scenarios matched. Use --list to see available scenarios.");
    return 2;
}

EvaluationDatasetSnapshot dataset;
try
{
    dataset = EvaluationDataset.LoadSnapshot(datasetPath);
}
catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or JsonException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
if (validateDataset)
{
    Console.WriteLine($"Valid dataset: {dataset.Name} ({dataset.Conversations.Count} conversations, {dataset.Questions.Count} questions)");
    return 0;
}
if (selfTest)
{
    // The self-test validates dataset integrity and PostgreSQL plumbing with the
    // deterministic local provider; it needs no API key and reports retrieval only.
    var defaultSelfTestScenarios = new[]
    {
        "baseline",
        "realistic-long-haul",
        "stale-forget",
        "strict-threshold"
    };

    // Behavior-aware extraction paths (for example PersonalMemory) require a live
    // LLM-backed extractor; deterministic self-test mode intentionally omits them.

    if (scenarioFilter.Count == 0)
    {
        selected = Scenarios.All
            .Where(scenario => defaultSelfTestScenarios.Contains(scenario.Name, StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }

    Console.WriteLine($"Self-test mode: deterministic local embeddings, retrieval metrics only, scenarios: {string.Join(", ", selected.Select(s => s.Name))}.");
}

EvaluationConfiguration configuration;
OpenAiCompatibleClient? provider = null;
LlmEvalHelper? llmHelper = null;

try
{
    if (selfTest)
    {
        configuration = File.Exists(configPath)
            ? EvaluationConfiguration.Load(configPath)
            : new EvaluationConfiguration();
    }
    else
    {
        configuration = EvaluationConfiguration.Load(configPath);
        var httpClient = new HttpClient { BaseAddress = new Uri(configuration.OpenAi.Endpoint, UriKind.Absolute) };
        provider = new OpenAiCompatibleClient(
            httpClient,
            configuration.OpenAi.ApiKey,
            configuration.OpenAi.ChatModel,
            configuration.OpenAi.EmbeddingModel);
        var judgeClient = string.Equals(configuration.OpenAi.JudgeModel, configuration.OpenAi.ChatModel, StringComparison.Ordinal)
            ? provider
            : new OpenAiCompatibleClient(httpClient, configuration.OpenAi.ApiKey, configuration.OpenAi.JudgeModel, configuration.OpenAi.EmbeddingModel);
        llmHelper = new LlmEvalHelper(provider, judgeClient);
    }
}
catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

Console.WriteLine($"Running {selected.Count} scenario(s) against PostgreSQL at {configuration.Postgres.ConnectionString.Split(';')[0]}...");
Console.WriteLine();

var reports = new List<ScenarioReport>();
foreach (var scenario in selected)
{
    Console.WriteLine($"[scenario] {scenario.Name}: {scenario.Description}");
    try
    {
        var runner = new ScenarioRunner(configuration, scenario, provider, llmHelper, selfTest, dataset);
        var report = await runner.RunAsync(CancellationToken.None);
        reports.Add(report);
        var accuracy = report.Accuracy is null ? "n/a (retrieval-only)" : $"{report.Accuracy.Value:P0} ({report.Correct}/{report.Judged})";
        var f1 = report.MeanF1 is null ? "" : $" | F1: {report.MeanF1.Value:F2}";
        Console.WriteLine($"  memories: {report.MemoriesStored} | accuracy: {accuracy}{f1} | retrieval hit rate: {report.RetrievalHitRate:P0} | mean search: {report.MeanSearchLatencyMs:F0} ms");
    }
    catch (Exception exception)
    {
        reports.Add(new ScenarioReport
        {
            Name = scenario.Name,
            Description = scenario.Description,
            MemoriesStored = 0,
            IngestSeconds = 0,
            Questions = 0,
            Judged = 0,
            Correct = 0,
            RetrievalHitRate = 0,
            MeanSearchLatencyMs = 0,
            Error = exception.Message
        });
        Console.WriteLine($"  FAILED: {exception.Message}");
    }
    Console.WriteLine();
}

var report_ = new EvaluationReport
{
    Timestamp = DateTimeOffset.UtcNow,
    Mode = selfTest ? "retrieval-only (deterministic local provider)" : "full (LLM extraction, answering, and judging)",
    ChatModel = selfTest ? null : configuration.OpenAi.ChatModel,
    EmbeddingModel = selfTest ? "local-deterministic" : configuration.OpenAi.EmbeddingModel,
    JudgeModel = selfTest ? null : configuration.OpenAi.JudgeModel,
    Store = "PostgreSQL/pgvector",
    Dataset = dataset.Name,
    ConversationCount = dataset.Conversations.Count,
    QuestionCount = dataset.Questions.Count,
    SyntheticDataset = datasetPath is null,
    ScenarioReports = reports
};

var resultsDirectory = Path.Combine(AppContext.BaseDirectory, "results");
var markdownPath = ReportWriter.Write(report_, resultsDirectory);
Console.WriteLine($"Report written to {markdownPath}");
return reports.Any(report => report.Error is not null) ? 1 : 0;

static void PrintUsage()
{
    Console.Error.WriteLine("""
        Usage:
          dotnet run --project evaluation/Mem0Sharp.Evaluation [--scenario name[,name]] [--config path] [--dataset path] [--validate-dataset] [--self-test] [--list]

        Options:
          --scenario   Run only the named scenarios (default: all).
          --config     Path to evalconfig.local.yaml (default: next to the executable).
          --dataset    Path to a JSON dataset; defaults to the built-in fictional fixture.
          --validate-dataset  Validate the selected JSON dataset without starting PostgreSQL.
          --self-test  Validate the harness with deterministic local embeddings; no API key needed.
          --list       List available scenarios.
        """);
}
