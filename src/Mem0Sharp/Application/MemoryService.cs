using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Mem0Sharp;

public sealed class MemoryService : IMemoryService
{
    private readonly IMemoryStore store;
    private readonly IEmbeddingGenerator embeddings;
    private readonly IMemoryExtractor extractor;
    private readonly IMemoryReranker? reranker;
    private readonly IMemoryConflictResolver? conflictResolver;
    private readonly IProceduralMemoryGenerator? proceduralMemoryGenerator;
    private readonly IEntityExtractor entityExtractor;
    private readonly IEntityStore entityStore;
    private readonly IGraphMemoryExtractor? graphExtractor;
    private readonly IGraphMemoryStore? graphStore;
    private readonly MemoryOptions options;
    private readonly Dictionary<string, float[]> vectors = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim indexLock = new(1, 1);

    public MemoryService(IMemoryStore? store = null, IEmbeddingGenerator? embeddings = null, IMemoryExtractor? extractor = null, MemoryOptions? options = null, IMemoryReranker? reranker = null, IMemoryConflictResolver? conflictResolver = null, IProceduralMemoryGenerator? proceduralMemoryGenerator = null, IEntityExtractor? entityExtractor = null, IEntityStore? entityStore = null, IGraphMemoryExtractor? graphExtractor = null, IGraphMemoryStore? graphStore = null)
    {
        this.store = store ?? new InMemoryStore();
        this.embeddings = embeddings ?? new LocalEmbeddingGenerator();
        this.extractor = extractor ?? new BasicMemoryExtractor();
        this.options = options ?? new MemoryOptions();
        this.reranker = reranker;
        this.conflictResolver = conflictResolver;
        this.proceduralMemoryGenerator = proceduralMemoryGenerator;
        this.entityExtractor = entityExtractor ?? new RuleBasedEntityExtractor();
        this.entityStore = entityStore ?? new InMemoryEntityStore();
        this.graphExtractor = graphExtractor;
        this.graphStore = graphStore;
    }

    public async Task<AddResult> AddAsync(string text, string userId = "default_user", string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        return await AddAsync(text, new MemoryAddOptions { UserId = userId, AgentId = agentId, RunId = runId, Scope = scope, Metadata = metadata }, cancellationToken);
    }

    public async Task<AddResult> AddAsync(IEnumerable<Message> messages, string userId = "default_user", string? agentId = null, string? runId = null, MemoryScope scope = MemoryScope.User, CancellationToken cancellationToken = default)
    {
        return await AddAsync(messages, new MemoryAddOptions { UserId = userId, AgentId = agentId, RunId = runId, Scope = scope }, cancellationToken);
    }

    public Task<AddResult> AddAsync(string text, MemoryAddOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Infer && options.Behavior != MemoryBehavior.Normal)
        {
            return AddAsync([new Message("user", text)], options, cancellationToken);
        }
        return SaveInputsAsync([new MemoryInput(text, options.Scope, options.Metadata, options.ExpiresAt, options.Behavior, options.MemoryType)], options, cancellationToken);
    }

    public async Task<AddResult> AddAsync(IEnumerable<Message> messages, MemoryAddOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(options);
        var materialized = messages.ToArray();
        if (string.Equals(options.MemoryType, "procedural_memory", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.AgentId)) throw new ArgumentException("Procedural memory requires an AgentId.", nameof(options));
            if (proceduralMemoryGenerator is null) throw new InvalidOperationException("Procedural memory requires an IProceduralMemoryGenerator.");
            var procedure = await proceduralMemoryGenerator.GenerateAsync(materialized, options.Prompt, cancellationToken);
            return await SaveInputsAsync([new MemoryInput(procedure, MemoryScope.Agent, Behavior: options.Behavior, MemoryType: "procedural_memory")], options with { Scope = MemoryScope.Agent }, cancellationToken);
        }
        if (options.Infer && conflictResolver is not null)
        {
            var existing = await GetAllAsync(CreateScopeFilter(options), cancellationToken);
            var decisions = await conflictResolver.ResolveAsync(materialized, existing, options, cancellationToken);
            return await ApplyDecisionsAsync(decisions, options, cancellationToken);
        }
        IReadOnlyList<MemoryInput> inputs = options.Infer
            ? await ExtractAsync(materialized, options, cancellationToken)
            : materialized.Where(message => !string.IsNullOrWhiteSpace(message.Content))
                .Select(message => new MemoryInput(message.Content.Trim(), Metadata: new Dictionary<string, string> { ["role"] = message.Role }))
                .ToArray();
        return await SaveInputsAsync(inputs.Select(input => input with { Scope = options.Scope, Behavior = options.Behavior, MemoryType = options.MemoryType ?? input.MemoryType }), options, cancellationToken);
    }

    private Task<IReadOnlyList<MemoryInput>> ExtractAsync(IReadOnlyList<Message> messages, MemoryAddOptions addOptions, CancellationToken cancellationToken)
    {
        if (addOptions.Behavior == MemoryBehavior.Normal)
        {
            return extractor.ExtractAsync(messages, cancellationToken);
        }
        if (extractor is IBehaviorAwareMemoryExtractor behaviorAwareExtractor)
        {
            return behaviorAwareExtractor.ExtractAsync(messages, addOptions, cancellationToken);
        }
        throw new NotSupportedException($"Memory behavior '{addOptions.Behavior}' requires an IBehaviorAwareMemoryExtractor.");
    }

    public Task<AddResult> AddManyAsync(IEnumerable<string> texts, MemoryAddOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var addOptions = options ?? new MemoryAddOptions();
        return SaveInputsAsync(texts.Select(text => new MemoryInput(text, addOptions.Scope, addOptions.Metadata, addOptions.ExpiresAt, addOptions.Behavior, addOptions.MemoryType)), addOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, MemoryFilter? filter = null, int? topK = null, CancellationToken cancellationToken = default)
    {
        return await SearchAsync(query, new MemorySearchOptions { Filter = filter, TopK = topK ?? options.DefaultTopK, Threshold = options.MinimumScore, Hybrid = options.EnableHybridSearch }, cancellationToken);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, MemorySearchOptions searchOptions, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(searchOptions);
        if (searchOptions.TopK < 0) throw new ArgumentOutOfRangeException(nameof(searchOptions));
        var effectiveOptions = searchOptions with { Filter = ApplySearchFilter(searchOptions) };
        var queryVector = await embeddings.GenerateAsync(query, cancellationToken);
        IReadOnlyList<SearchResult> semanticResults;
        if (store is IVectorMemoryStore vectorStore)
        {
            semanticResults = await vectorStore.SearchAsync(queryVector, effectiveOptions.Filter, Math.Max(searchOptions.TopK * 4, 60), cancellationToken);
        }
        else
        {
            var candidates = new List<Memory>();
            await foreach (var memory in store.GetAllAsync(effectiveOptions.Filter, cancellationToken))
            {
                if (candidates.Count == options.MaxCandidateCount) break;
                candidates.Add(memory);
            }
            var results = new List<SearchResult>(candidates.Count);
            foreach (var memory in candidates)
            {
                var score = CosineSimilarity(queryVector, await GetVectorAsync(memory, cancellationToken));
                results.Add(new SearchResult(memory, score));
            }
            semanticResults = results;
        }

        return await RankSearchResultsAsync(query, effectiveOptions, semanticResults, cancellationToken);
    }

    private async Task<IReadOnlyList<SearchResult>> RankSearchResultsAsync(string query, MemorySearchOptions searchOptions, IReadOnlyList<SearchResult> semanticResults, CancellationToken cancellationToken)
    {
        var queryEntities = await entityExtractor.ExtractAsync(query, cancellationToken);
        var entityBoosts = new Dictionary<string, double>(await entityStore.GetMemoryBoostsAsync(queryEntities, cancellationToken), StringComparer.Ordinal);
        if (graphStore is not null)
        {
            foreach (var boost in await graphStore.GetMemoryBoostsAsync(query, cancellationToken))
            {
                entityBoosts[boost.Key] = Math.Min(0.5, entityBoosts.GetValueOrDefault(boost.Key) + boost.Value);
            }
        }
        IReadOnlyList<SearchResult> ranked = searchOptions.Hybrid
            ? HybridSearchScorer.ScoreAndRank(query, semanticResults, entityBoosts, searchOptions.Threshold, searchOptions.TopK, searchOptions.Explain)
            : semanticResults.Where(result => result.Score >= searchOptions.Threshold)
                .OrderByDescending(result => result.Score)
                .Take(searchOptions.TopK)
                .Select(result => searchOptions.Explain ? result with { ScoreDetails = new SearchScoreDetails(result.Score, Threshold: searchOptions.Threshold) } : result with { ScoreDetails = null })
                .ToArray();
        if (searchOptions.Rerank && reranker is not null)
        {
            ranked = await reranker.RerankAsync(query, ranked, searchOptions.TopK, cancellationToken);
        }
        if (searchOptions.RecencyBias > 0)
        {
            ranked = ApplyRecencyBias(ranked, searchOptions);
        }
        return ranked;
    }

    private static IReadOnlyList<SearchResult> ApplyRecencyBias(IReadOnlyList<SearchResult> ranked, MemorySearchOptions searchOptions)
    {
        var recencyBias = Math.Clamp(searchOptions.RecencyBias, 0d, 1d);
        if (recencyBias <= 0) return ranked;
        var window = searchOptions.FreshnessWindow ?? TimeSpan.FromDays(30);
        if (window <= TimeSpan.Zero) window = TimeSpan.FromDays(30);
        var now = DateTimeOffset.UtcNow;

        return ranked
            .Select(result =>
            {
                var age = now - result.Memory.UpdatedAt;
                var freshness = age <= TimeSpan.Zero ? 1d : Math.Clamp(1d - age.TotalSeconds / window.TotalSeconds, 0d, 1d);
                var boostedScore = result.Score * (1d - recencyBias) + freshness * recencyBias;
                return result with { Score = boostedScore };
            })
            .OrderByDescending(result => result.Score)
            .ToArray();
    }

    public async Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchManyAsync(IEnumerable<string> queries, MemoryFilter? filter = null, int? topK = null, CancellationToken cancellationToken = default)
    {
        return await SearchManyAsync(queries, new MemorySearchOptions { Filter = filter, TopK = topK ?? options.DefaultTopK, Threshold = options.MinimumScore, Hybrid = options.EnableHybridSearch }, cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyList<SearchResult>>> SearchManyAsync(IEnumerable<string> queries, MemorySearchOptions searchOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(searchOptions);
        var materialized = queries.ToArray();
        foreach (var query in materialized) ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (materialized.Length == 0) return [];

        searchOptions = searchOptions with { Filter = ApplySearchFilter(searchOptions) };
        if (searchOptions.TopK < 0) throw new ArgumentOutOfRangeException(nameof(searchOptions.TopK));
        if (embeddings is IBatchEmbeddingGenerator batchEmbeddings && store is IBatchVectorMemoryStore batchVectorStore)
        {
            var queryVectors = await batchEmbeddings.GenerateBatchAsync(materialized, cancellationToken);
            if (queryVectors.Count != materialized.Length) throw new InvalidOperationException("The embedding provider returned a different number of vectors than input queries.");
            var semanticBatches = await batchVectorStore.SearchBatchAsync(queryVectors, searchOptions.Filter, Math.Max(searchOptions.TopK * 4, 60), cancellationToken);
            if (semanticBatches.Count != materialized.Length) throw new InvalidOperationException("The vector store returned a different number of result sets than input queries.");

            var batchedResults = new IReadOnlyList<SearchResult>[materialized.Length];
            for (var index = 0; index < materialized.Length; index++)
            {
                batchedResults[index] = await RankSearchResultsAsync(materialized[index], searchOptions, semanticBatches[index], cancellationToken);
            }
            return batchedResults;
        }

        var results = new List<IReadOnlyList<SearchResult>>(materialized.Length);
        foreach (var query in materialized) results.Add(await SearchAsync(query, searchOptions, cancellationToken));
        return results;
    }

    public Task<Memory?> GetAsync(string id, CancellationToken cancellationToken = default) => store.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<MemoryHistoryEntry>> GetHistoryAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return store is IMemoryHistoryStore historyStore
            ? historyStore.GetHistoryAsync(id, cancellationToken)
            : Task.FromResult<IReadOnlyList<MemoryHistoryEntry>>([]);
    }

    public async Task<IReadOnlyList<Memory>> GetAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var result = new List<Memory>();
        await foreach (var memory in store.GetAllAsync(filter, cancellationToken)) result.Add(memory);
        return result;
    }

    public async Task<MemoryPage> GetPageAsync(MemoryPageOptions pageOptions, MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pageOptions);
        if (pageOptions.Offset < 0) throw new ArgumentOutOfRangeException(nameof(pageOptions));
        if (pageOptions.Limit < 0) throw new ArgumentOutOfRangeException(nameof(pageOptions));
        var memories = await GetAllAsync(filter, cancellationToken);
        return new MemoryPage(memories.Skip(pageOptions.Offset).Take(pageOptions.Limit).ToArray(), memories.Count, pageOptions.Offset, pageOptions.Limit);
    }

    public async Task<int> ForgetStaleAsync(TimeSpan retentionWindow, MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        if (retentionWindow < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retentionWindow));
        var cutoff = DateTimeOffset.UtcNow - retentionWindow;
        var stale = (await GetAllAsync(filter is null ? new MemoryFilter(IncludeExpired: true) : filter with { IncludeExpired = true }, cancellationToken))
            .Where(memory => memory.UpdatedAt < cutoff || memory.CreatedAt < cutoff || (memory.ExpiresAt.HasValue && memory.ExpiresAt.Value < DateTimeOffset.UtcNow))
            .ToArray();
        foreach (var memory in stale) await DeleteAsync(memory.Id, cancellationToken);
        return stale.Length;
    }

    public async Task<IReadOnlyList<Memory>> ConsolidateAsync(MemoryFilter? filter = null, int maxItems = 10, CancellationToken cancellationToken = default)
    {
        if (maxItems < 0) throw new ArgumentOutOfRangeException(nameof(maxItems));
        var memories = (await GetAllAsync(filter, cancellationToken))
            .OrderByDescending(memory => memory.UpdatedAt)
            .Take(maxItems)
            .ToArray();
        if (memories.Length == 0) return [];

        var summaryText = string.Join(" ", memories.Select(memory => memory.Text).Where(text => !string.IsNullOrWhiteSpace(text)));
        if (string.IsNullOrWhiteSpace(summaryText)) return [];

        var scope = filter?.Scope ?? MemoryScope.User;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["summary_source_count"] = memories.Length.ToString(CultureInfo.InvariantCulture),
            ["summary_window_start"] = memories.Min(memory => memory.CreatedAt).ToString("O"),
            ["summary_window_end"] = memories.Max(memory => memory.UpdatedAt).ToString("O"),
            ["summary_generated_at"] = DateTimeOffset.UtcNow.ToString("O")
        };

        var result = await SaveInputsAsync(
            [new MemoryInput(summaryText, scope, metadata, Behavior: MemoryBehavior.Normal, MemoryType: "consolidated_memory")],
            new MemoryAddOptions
            {
                UserId = filter?.UserId ?? "default_user",
                AgentId = filter?.AgentId,
                RunId = filter?.RunId,
                Scope = scope,
                Metadata = metadata,
                Infer = false,
                MemoryType = "consolidated_memory"
            },
            cancellationToken);
        return result.Memories;
    }

    public async Task<Memory> UpdateAsync(string id, string text, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        return await UpdateAsync(id, new MemoryUpdate { Text = text, Metadata = metadata }, cancellationToken);
    }

    public async Task<Memory> UpdateAsync(string id, MemoryUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.Text is not null) ArgumentException.ThrowIfNullOrWhiteSpace(update.Text);
        var existing = await store.GetAsync(id, cancellationToken) ?? throw new KeyNotFoundException($"Memory '{id}' was not found.");
        var updated = existing with
        {
            Text = update.Text ?? existing.Text,
            Metadata = update.Metadata ?? existing.Metadata,
            ExpiresAt = update.UpdateExpiration ? update.ExpiresAt : existing.ExpiresAt,
            Hash = update.Text is null ? existing.Hash : ComputeHash(update.Text),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var updatedVector = await embeddings.GenerateAsync(updated.Text, cancellationToken);
        var enrichment = update.Text is null ? null : await PrepareEnrichmentAsync(updated.Text, cancellationToken);
        var history = CreateHistoryEntry(updated, MemoryHistoryEvent.Update, existing.Text, updated.Text);
        if (store is IAtomicMemoryStore atomicStore)
        {
            await atomicStore.SaveBatchWithHistoryAsync([new MemoryWriteRecord(updated, updatedVector, history!)], cancellationToken);
        }
        else if (store is IVectorMemoryStore vectorStore)
        {
            await vectorStore.SaveAsync(updated, updatedVector, cancellationToken);
            if (history is not null) await ((IMemoryHistoryStore)store).SaveHistoryAsync(history, cancellationToken);
        }
        else
        {
            await store.SaveAsync(updated, cancellationToken);
            if (history is not null) await ((IMemoryHistoryStore)store).SaveHistoryAsync(history, cancellationToken);
        }
        await indexLock.WaitAsync(cancellationToken);
        try { vectors[id] = updatedVector.ToArray(); }
        finally { indexLock.Release(); }
        if (enrichment is not null)
        {
            await ApplyEnrichmentAsync(enrichment, id, cancellationToken);
        }
        return updated;
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var existing = await store.GetAsync(id, cancellationToken);
        if (existing is not null) await RemoveEnrichmentAsync(id, cancellationToken);
        var history = existing is null ? null : CreateHistoryEntry(existing, MemoryHistoryEvent.Delete, existing.Text, null, DateTimeOffset.UtcNow, true);
        if (store is IAtomicMemoryStore atomicStore && history is not null)
        {
            await atomicStore.DeleteWithHistoryAsync(id, history, cancellationToken);
        }
        else
        {
            await store.DeleteAsync(id, cancellationToken);
            if (history is not null) await ((IMemoryHistoryStore)store).SaveHistoryAsync(history, cancellationToken);
        }
        await indexLock.WaitAsync(cancellationToken);
        try { vectors.Remove(id); }
        finally { indexLock.Release(); }
    }

    public async Task<int> DeleteAllAsync(MemoryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var memories = await GetAllAsync(filter, cancellationToken);
        if (store is IAtomicMemoryStore || store is IBulkMemoryStore)
        {
            foreach (var memory in memories) await RemoveEnrichmentAsync(memory.Id, cancellationToken);
            if (store is IAtomicMemoryStore atomicStore)
            {
                var records = memories.Select(memory => new MemoryDeleteRecord(memory, CreateHistoryEntry(memory, MemoryHistoryEvent.Delete, memory.Text, null, DateTimeOffset.UtcNow, true)!)).ToArray();
                await atomicStore.DeleteAllWithHistoryAsync(records, cancellationToken);
                foreach (var memory in memories) await RemoveVectorAsync(memory.Id, cancellationToken);
                return memories.Count;
            }

            var bulkStore = (IBulkMemoryStore)store;
            var deleted = await bulkStore.DeleteAllAsync(filter, cancellationToken);
            foreach (var memory in memories)
            {
                await RemoveVectorAsync(memory.Id, cancellationToken);
                await SaveHistoryAsync(memory, MemoryHistoryEvent.Delete, memory.Text, null, cancellationToken, DateTimeOffset.UtcNow, true);
            }
            return deleted;
        }
        foreach (var memory in memories) await DeleteAsync(memory.Id, cancellationToken);
        return memories.Count;
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        if (store is IResettableMemoryStore resettableStore)
        {
            await resettableStore.ResetAsync(cancellationToken);
        }
        else
        {
            await DeleteAllAsync(new MemoryFilter(IncludeExpired: true), cancellationToken);
        }
        await indexLock.WaitAsync(cancellationToken);
        try { vectors.Clear(); }
        finally { indexLock.Release(); }
        await entityStore.ResetAsync(cancellationToken);
        if (graphStore is not null) await graphStore.ResetAsync(cancellationToken);
    }

    private async Task<AddResult> SaveInputsAsync(IEnumerable<MemoryInput> inputs, MemoryAddOptions addOptions, CancellationToken cancellationToken)
    {
        var saved = new List<Memory>();
        var actions = new List<MemoryActionResult>();
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        if (addOptions.Deduplicate)
        {
            await foreach (var existing in store.GetAllAsync(CreateScopeFilter(addOptions), cancellationToken))
            {
                hashes.Add(string.IsNullOrEmpty(existing.Hash) ? ComputeHash(existing.Text) : existing.Hash);
            }
        }
        var pending = new List<Memory>();
        foreach (var input in inputs.Where(item => !string.IsNullOrWhiteSpace(item.Text)))
        {
            var text = input.Text.Trim();
            var hash = ComputeHash(text);
            if (addOptions.Deduplicate && !hashes.Add(hash))
            {
                actions.Add(new MemoryActionResult(null, text, MemoryAction.None));
                continue;
            }
            var now = DateTimeOffset.UtcNow;
            var metadata = new Dictionary<string, string>(addOptions.Metadata ?? new Dictionary<string, string>());
            if (input.Metadata is not null)
            {
                foreach (var item in input.Metadata) metadata[item.Key] = item.Value;
            }
            pending.Add(new Memory { Id = Guid.NewGuid().ToString("N"), Text = text, UserId = addOptions.UserId, AgentId = addOptions.AgentId, RunId = addOptions.RunId, Scope = input.Scope, Metadata = metadata, CreatedAt = now, UpdatedAt = now, ExpiresAt = input.ExpiresAt ?? addOptions.ExpiresAt, Hash = hash, Behavior = input.Behavior, MemoryType = input.MemoryType });
        }

        IReadOnlyList<IReadOnlyList<float>> generatedVectors = embeddings is IBatchEmbeddingGenerator batchEmbeddings
            ? await batchEmbeddings.GenerateBatchAsync(pending.Select(memory => memory.Text).ToArray(), cancellationToken)
            : await GenerateVectorsAsync(pending, cancellationToken);
        if (generatedVectors.Count != pending.Count) throw new InvalidOperationException("The embedding provider returned a different number of vectors than input texts.");

        var records = pending.Select((memory, index) => new MemoryVectorRecord(memory, generatedVectors[index])).ToArray();
        var enrichments = new Dictionary<string, MemoryEnrichment>(StringComparer.Ordinal);
        foreach (var record in records) enrichments[record.Memory.Id] = await PrepareEnrichmentAsync(record.Memory.Text, cancellationToken);
        var historyEntries = records.Select(record => CreateHistoryEntry(record.Memory, MemoryHistoryEvent.Add, null, record.Memory.Text)).ToArray();
        if (store is IAtomicMemoryStore atomicStore)
        {
            await atomicStore.SaveBatchWithHistoryAsync(records.Select((record, index) => new MemoryWriteRecord(record.Memory, record.Embedding, historyEntries[index]!)).ToArray(), cancellationToken);
        }
        else if (store is IBatchVectorMemoryStore batchVectorStore) await batchVectorStore.SaveBatchAsync(records, cancellationToken);
        else if (store is IVectorMemoryStore vectorStore)
        {
            foreach (var record in records) await vectorStore.SaveAsync(record.Memory, record.Embedding, cancellationToken);
        }
        else if (store is IBatchMemoryStore batchStore) await batchStore.SaveBatchAsync(pending, cancellationToken);
        else
        {
            foreach (var memory in pending) await store.SaveAsync(memory, cancellationToken);
        }

        foreach (var record in records)
        {
            var memory = record.Memory;
            await indexLock.WaitAsync(cancellationToken);
            try { vectors[memory.Id] = record.Embedding.ToArray(); }
            finally { indexLock.Release(); }
            if (store is not IAtomicMemoryStore) await SaveHistoryAsync(memory, MemoryHistoryEvent.Add, null, memory.Text, cancellationToken);
            await ApplyEnrichmentAsync(enrichments[memory.Id], memory.Id, cancellationToken);
            saved.Add(memory);
            actions.Add(new MemoryActionResult(memory.Id, memory.Text, MemoryAction.Add));
        }
        return new AddResult(saved, actions);
    }

    private async Task<IReadOnlyList<IReadOnlyList<float>>> GenerateVectorsAsync(IReadOnlyList<Memory> memories, CancellationToken cancellationToken)
    {
        var generated = new IReadOnlyList<float>[memories.Count];
        for (var index = 0; index < memories.Count; index++) generated[index] = await embeddings.GenerateAsync(memories[index].Text, cancellationToken);
        return generated;
    }

    private async Task<AddResult> ApplyDecisionsAsync(IReadOnlyList<MemoryDecision> decisions, MemoryAddOptions addOptions, CancellationToken cancellationToken)
    {
        var memories = new List<Memory>();
        var actions = new List<MemoryActionResult>();
        foreach (var decision in decisions)
        {
            switch (decision.Event)
            {
                case MemoryAction.Add:
                    var added = await SaveInputsAsync([new MemoryInput(decision.Text, addOptions.Scope, decision.Metadata, Behavior: addOptions.Behavior, MemoryType: addOptions.MemoryType)], addOptions, cancellationToken);
                    memories.AddRange(added.Memories);
                    actions.AddRange(added.Actions ?? []);
                    break;
                case MemoryAction.Update when decision.MemoryId is not null:
                    var updated = await UpdateAsync(decision.MemoryId, new MemoryUpdate { Text = decision.Text, Metadata = decision.Metadata }, cancellationToken);
                    memories.Add(updated);
                    actions.Add(new MemoryActionResult(updated.Id, updated.Text, MemoryAction.Update));
                    break;
                case MemoryAction.Delete when decision.MemoryId is not null:
                    var deleted = await GetAsync(decision.MemoryId, cancellationToken);
                    await DeleteAsync(decision.MemoryId, cancellationToken);
                    actions.Add(new MemoryActionResult(decision.MemoryId, deleted?.Text, MemoryAction.Delete));
                    break;
                default:
                    actions.Add(new MemoryActionResult(decision.MemoryId, decision.Text, MemoryAction.None));
                    break;
            }
        }
        return new AddResult(memories, actions);
    }

    private static MemoryFilter CreateScopeFilter(MemoryAddOptions addOptions) => new(addOptions.UserId, addOptions.AgentId, addOptions.RunId, addOptions.Scope);

    private static MemoryFilter? ApplySearchFilter(MemorySearchOptions searchOptions)
    {
        if (searchOptions.Behavior is not null)
        {
            return (searchOptions.Filter ?? new MemoryFilter()) with { Behavior = searchOptions.Behavior };
        }
        if (searchOptions.IncludeNonFactual || searchOptions.Filter?.Behavior is not null) return searchOptions.Filter;
        return (searchOptions.Filter ?? new MemoryFilter()) with { Behavior = MemoryBehavior.Normal };
    }

    private static string ComputeHash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public Task<IReadOnlyList<MemoryRelation>> GetRelationsAsync(string? query = null, CancellationToken cancellationToken = default) =>
        graphStore?.GetRelationsAsync(query, cancellationToken) ?? Task.FromResult<IReadOnlyList<MemoryRelation>>([]);

    private async Task<MemoryEnrichment> PrepareEnrichmentAsync(string text, CancellationToken cancellationToken)
    {
        var entities = await entityExtractor.ExtractAsync(text, cancellationToken);
        var relations = graphStore is not null && graphExtractor is not null
            ? await graphExtractor.ExtractAsync(text, cancellationToken)
            : [];
        return new MemoryEnrichment(entities, relations);
    }

    private async Task ApplyEnrichmentAsync(MemoryEnrichment enrichment, string memoryId, CancellationToken cancellationToken)
    {
        try
        {
            await entityStore.RemoveMemoryAsync(memoryId, cancellationToken);
            await entityStore.UpsertLinksAsync(enrichment.Entities, memoryId, cancellationToken);
            if (graphStore is not null)
            {
                await graphStore.RemoveMemoryAsync(memoryId, cancellationToken);
                if (graphExtractor is not null) await graphStore.UpsertAsync(enrichment.Relations, memoryId, cancellationToken);
            }
        }
        catch
        {
            await RemoveEnrichmentAfterFailureAsync(memoryId, cancellationToken);
            throw;
        }
    }

    private async Task RemoveEnrichmentAsync(string memoryId, CancellationToken cancellationToken)
    {
        await entityStore.RemoveMemoryAsync(memoryId, cancellationToken);
        if (graphStore is not null) await graphStore.RemoveMemoryAsync(memoryId, cancellationToken);
    }

    private async Task RemoveEnrichmentAfterFailureAsync(string memoryId, CancellationToken cancellationToken)
    {
        try { await entityStore.RemoveMemoryAsync(memoryId, cancellationToken); }
        catch { }
        if (graphStore is not null)
        {
            try { await graphStore.RemoveMemoryAsync(memoryId, cancellationToken); }
            catch { }
        }
    }

    private sealed record MemoryEnrichment(IReadOnlyList<ExtractedEntity> Entities, IReadOnlyList<ExtractedRelation> Relations);

    private MemoryHistoryEntry? CreateHistoryEntry(Memory memory, MemoryHistoryEvent eventType, string? oldMemory, string? newMemory, DateTimeOffset? updatedAt = null, bool isDeleted = false)
    {
        if (store is not IMemoryHistoryStore) return null;
        return new MemoryHistoryEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            MemoryId = memory.Id,
            Event = eventType,
            OldMemory = oldMemory,
            NewMemory = newMemory,
            CreatedAt = memory.CreatedAt,
            UpdatedAt = updatedAt ?? memory.UpdatedAt,
            IsDeleted = isDeleted,
            ActorId = memory.Metadata.TryGetValue("actor_id", out var actorId) ? actorId : null,
            Role = memory.Metadata.TryGetValue("role", out var role) ? role : null
        };
    }

    private Task SaveHistoryAsync(Memory memory, MemoryHistoryEvent eventType, string? oldMemory, string? newMemory, CancellationToken cancellationToken, DateTimeOffset? updatedAt = null, bool isDeleted = false)
    {
        var entry = CreateHistoryEntry(memory, eventType, oldMemory, newMemory, updatedAt, isDeleted);
        return entry is null ? Task.CompletedTask : ((IMemoryHistoryStore)store).SaveHistoryAsync(entry, cancellationToken);
    }

    private async Task RemoveVectorAsync(string memoryId, CancellationToken cancellationToken)
    {
        await indexLock.WaitAsync(cancellationToken);
        try { vectors.Remove(memoryId); }
        finally { indexLock.Release(); }
    }

    private async Task<float[]> GetVectorAsync(Memory memory, CancellationToken cancellationToken)
    {
        await indexLock.WaitAsync(cancellationToken);
        try
        {
            if (vectors.TryGetValue(memory.Id, out var vector)) return vector;
            vector = (await embeddings.GenerateAsync(memory.Text, cancellationToken)).ToArray();
            vectors[memory.Id] = vector;
            return vector;
        }
        finally { indexLock.Release(); }
    }

    private static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count != right.Count) return 0;
        double dot = 0, leftNorm = 0, rightNorm = 0;
        for (var index = 0; index < left.Count; index++) { dot += left[index] * right[index]; leftNorm += left[index] * left[index]; rightNorm += right[index] * right[index]; }
        return leftNorm == 0 || rightNorm == 0 ? 0 : dot / Math.Sqrt(leftNorm * rightNorm);
    }
}