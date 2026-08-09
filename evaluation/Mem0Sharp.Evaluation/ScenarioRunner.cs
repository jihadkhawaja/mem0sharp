using System.Diagnostics;

namespace Mem0Sharp.Evaluation;

/// <summary>
/// Runs one scenario: fresh PostgreSQL tables, ingest all conversations with the
/// scenario's add options, then search, answer, and judge every question.
/// </summary>
internal sealed class ScenarioRunner(
    EvaluationConfiguration configuration,
    ScenarioDefinition scenario,
    OpenAiCompatibleClient? provider,
    LlmEvalHelper? llmHelper,
    bool retrievalOnly)
{
    private readonly int topK = Math.Max(1, configuration.Evaluation.TopK);

    public async Task<ScenarioReport> RunAsync(CancellationToken cancellationToken)
    {
        var ingestWatch = Stopwatch.StartNew();
        var memoriesStored = 0;

        await using var store = new PostgresMemoryStore(new PostgresMemoryStoreOptions
        {
            ConnectionString = configuration.Postgres.ConnectionString,
            EmbeddingDimensions = configuration.Postgres.EmbeddingDimensions,
            TableName = scenario.TableName,
            UseHnswIndex = true,
            CreateExtension = true
        });
        await store.InitializeAsync(cancellationToken);
        await store.ResetAsync(cancellationToken);

        var memory = new MemoryService(
            store: store,
            embeddings: retrievalOnly ? new LocalEmbeddingGenerator(configuration.Postgres.EmbeddingDimensions) : provider!,
            extractor: retrievalOnly ? new BasicMemoryExtractor() : new LlmMemoryExtractor(provider!),
            reranker: scenario.Rerank && !retrievalOnly ? new LlmReranker(provider!) : null,
            conflictResolver: scenario.UseConflictResolver && !retrievalOnly ? new LlmMemoryConflictResolver(provider!) : null);

        // Ingest every conversation session-by-session under a scenario- and
        // conversation-scoped user id so scenarios never contaminate each other.
        foreach (var conversation in EvaluationDataset.Load())
        {
            foreach (var session in conversation.Sessions)
            {
                var messages = session.Turns
                    .Select(turn => new Message(
                        turn.Speaker == conversation.SpeakerA ? "user" : "assistant",
                        $"{turn.Speaker} ({session.Date}): {turn.Text}"))
                    .ToArray();

                var result = await memory.AddAsync(messages, new MemoryAddOptions
                {
                    UserId = $"eval-{scenario.Name}-{conversation.Id}",
                    AgentId = "evaluator",
                    Behavior = scenario.Behavior,
                    Prompt = scenario.BehaviorPersona,
                    Infer = scenario.Infer || retrievalOnly,
                    Deduplicate = scenario.Deduplicate,
                    Metadata = new Dictionary<string, string>
                    {
                        ["conversation"] = conversation.Id,
                        ["session_date"] = session.Date
                    }
                }, cancellationToken);
                memoriesStored += result.Memories.Count;
            }
        }
        ingestWatch.Stop();

        var questions = EvaluationDataset.Questions();
        var results = new QuestionResult[questions.Count];
        var gate = new SemaphoreSlim(Math.Max(1, configuration.Evaluation.Concurrency));
        var workers = questions.Select(async (question, index) =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                results[index] = await EvaluateQuestionAsync(memory, question, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(workers);

        return BuildReport(memoriesStored, ingestWatch.Elapsed, results);
    }

    private async Task<QuestionResult> EvaluateQuestionAsync(MemoryService memory, EvalQuestion question, CancellationToken cancellationToken)
    {
        try
        {
            return await EvaluateQuestionCoreAsync(memory, question, cancellationToken);
        }
        catch (Exception exception)
        {
            return new QuestionResult
            {
                QuestionId = question.Id,
                Category = question.Category,
                Question = question.Question,
                ExpectedAnswer = question.ExpectedAnswer,
                GeneratedAnswer = null,
                JudgeVerdict = $"ERROR: {exception.Message}",
                Correct = null,
                RetrievalHit = false,
                RetrievedCount = 0,
                SearchLatencyMs = 0,
                RetrievedMemories = []
            };
        }
    }

    private async Task<QuestionResult> EvaluateQuestionCoreAsync(MemoryService memory, EvalQuestion question, CancellationToken cancellationToken)
    {
        var conversation = EvaluationDataset.Load().Single(item => item.Id == question.ConversationId);
        var filter = new MemoryFilter(UserId: $"eval-{scenario.Name}-{conversation.Id}");

        var searchWatch = Stopwatch.StartNew();
        var searchResults = await memory.SearchAsync(question.Question, new MemorySearchOptions
        {
            Filter = filter,
            TopK = topK,
            Threshold = scenario.Threshold,
            Hybrid = scenario.Hybrid,
            Rerank = scenario.Rerank && !retrievalOnly,
            Explain = false
        }, cancellationToken);
        searchWatch.Stop();

        var retrievedTexts = searchResults.Select(result => result.Memory.Text).ToArray();
        var retrievalHit = !question.IsAdversarial
            && question.Evidence.Any(evidence =>
                retrievedTexts.Any(text => text.Contains(evidence, StringComparison.OrdinalIgnoreCase)));

        if (retrievalOnly)
        {
            return new QuestionResult
            {
                QuestionId = question.Id,
                Category = question.Category,
                Question = question.Question,
                ExpectedAnswer = question.ExpectedAnswer,
                RetrievalHit = retrievalHit,
                RetrievedCount = searchResults.Count,
                SearchLatencyMs = searchWatch.Elapsed.TotalMilliseconds,
                RetrievedMemories = retrievedTexts
            };
        }

        var generatedAnswer = await llmHelper!.GenerateAnswerAsync(question.Question, searchResults, cancellationToken);
        var outcome = await llmHelper.JudgeAsync(question.Question, question.ExpectedAnswer, generatedAnswer, cancellationToken);

        return new QuestionResult
        {
            QuestionId = question.Id,
            Category = question.Category,
            Question = question.Question,
            ExpectedAnswer = question.ExpectedAnswer,
            GeneratedAnswer = generatedAnswer,
            JudgeVerdict = outcome.Correct ? "CORRECT" : "WRONG",
            JudgeReasoning = outcome.Reasoning,
            Correct = outcome.Correct,
            F1 = outcome.F1,
            Bleu1 = outcome.Bleu1,
            RetrievalHit = retrievalHit,
            RetrievedCount = searchResults.Count,
            SearchLatencyMs = searchWatch.Elapsed.TotalMilliseconds,
            RetrievedMemories = retrievedTexts
        };
    }

    private ScenarioReport BuildReport(int memoriesStored, TimeSpan ingestElapsed, IReadOnlyList<QuestionResult> results)
    {
        var judged = results.Where(result => result.Correct.HasValue).ToArray();
        var answerable = results.Where(result => result.Category != EvaluationDataset.CategoryAdversarial).ToArray();

        var categories = EvaluationDataset.Categories.Select(category =>
        {
            var inCategory = results.Where(result => result.Category == category).ToArray();
            var judgedInCategory = inCategory.Where(result => result.Correct.HasValue).ToArray();
            var answerableInCategory = category == EvaluationDataset.CategoryAdversarial ? [] : inCategory;
            return new CategoryMetrics
            {
                Category = category,
                Questions = judgedInCategory.Length,
                Correct = judgedInCategory.Count(result => result.Correct == true),
                RetrievalHitRate = answerableInCategory.Length == 0
                    ? 0
                    : (double)answerableInCategory.Count(result => result.RetrievalHit) / answerableInCategory.Length
            };
        }).ToArray();

        return new ScenarioReport
        {
            Name = scenario.Name,
            Description = scenario.Description,
            MemoriesStored = memoriesStored,
            IngestSeconds = ingestElapsed.TotalSeconds,
            Questions = results.Count,
            Judged = judged.Length,
            Correct = judged.Count(result => result.Correct == true),
            MeanF1 = judged.Length == 0 ? null : judged.Average(result => result.F1 ?? 0),
            MeanBleu1 = judged.Length == 0 ? null : judged.Average(result => result.Bleu1 ?? 0),
            RetrievalHitRate = answerable.Length == 0
                ? 0
                : (double)answerable.Count(result => result.RetrievalHit) / answerable.Length,
            MeanSearchLatencyMs = results.Count == 0 ? 0 : results.Average(result => result.SearchLatencyMs),
            Categories = categories,
            Results = results
        };
    }
}
