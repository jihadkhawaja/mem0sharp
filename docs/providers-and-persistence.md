# Providers and persistence

Mem0Sharp separates the service API from embeddings, extraction, and storage. This lets the same application code run locally with deterministic components and in production with model-backed embeddings and a persistent database.

## OpenAI-compatible provider

`OpenAiCompatibleClient` implements both `IEmbeddingGenerator` and `IChatCompletionClient`. It sends requests to the `v1/embeddings` and `v1/chat/completions` paths relative to the supplied `HttpClient.BaseAddress`.

```csharp
using Mem0Sharp;

using var httpClient = new HttpClient
{
    BaseAddress = new Uri("https://api.openai.com/")
};

var provider = new OpenAiCompatibleClient(
    httpClient,
    Environment.GetEnvironmentVariable("OPENAI_API_KEY")!,
    chatModel: "gpt-5-mini",
    embeddingModel: "text-embedding-3-small");

var memory = new MemoryService(
    embeddings: provider,
    extractor: new LlmMemoryExtractor(provider));
```

The provider also works with compatible hosted or local servers. Set `BaseAddress` to the provider root and choose model names accepted by that server. The API key is sent as a Bearer token.

Keep the embedding model consistent for the lifetime of a vector store. Changing embedding models generally changes vector dimensions and makes existing vectors incompatible with the configured PostgreSQL column.

## PostgreSQL and pgvector

`PostgresMemoryStore` persists memory fields and embeddings in PostgreSQL. Install PostgreSQL with the `vector` extension available, then initialize the store once before using it:

```csharp
var provider = new OpenAiCompatibleClient(
    httpClient,
    Environment.GetEnvironmentVariable("OPENAI_API_KEY")!);

var store = new PostgresMemoryStore(new PostgresMemoryStoreOptions
{
    ConnectionString = Environment.GetEnvironmentVariable("MEM0_POSTGRES")!,
    EmbeddingDimensions = 1536,
    TableName = "mem0_memories",
    UseHnswIndex = true,
    CreateExtension = true
});

await store.InitializeAsync();

var memory = new MemoryService(
    store: store,
    embeddings: provider,
    extractor: new LlmMemoryExtractor(provider));
```

`EmbeddingDimensions` must exactly match the number of values returned by the embedding provider. The default OpenAI `text-embedding-3-small` model returns 1536 dimensions.

Initialization creates the table, a user index, and an HNSW cosine index when enabled and supported. HNSW creation is skipped automatically when the configured dimension is greater than 2000. Set `UseHnswIndex` to `false` when an HNSW index is not wanted.

Set `CreateExtension = false` when the database user cannot create extensions and the `vector` extension has already been installed by an administrator.

The table name must be a simple PostgreSQL identifier containing letters, numbers, and underscores, and beginning with a letter or underscore.

## Custom providers and stores

Implement `IEmbeddingGenerator` to connect another embedding service:

```csharp
public sealed class MyEmbeddingGenerator : IEmbeddingGenerator
{
    public Task<IReadOnlyList<float>> GenerateAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        // Call the embedding service and return one vector for text.
        throw new NotImplementedException();
    }
}
```

Implement `IMemoryStore` for CRUD storage. Add `IVectorMemoryStore` when the store can perform similarity search itself; otherwise `MemoryService` uses its local vector cache and scans up to `MaxCandidateCount` memories. Add `IBulkMemoryStore` when filtered deletion can be performed efficiently by the backend.

All custom implementations should honor cancellation tokens and return vectors with a stable dimension.
