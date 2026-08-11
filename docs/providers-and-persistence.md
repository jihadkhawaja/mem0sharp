# Providers and persistence

Mem0Sharp separates the service API from embeddings, extraction, and storage. This lets the same application code run locally with deterministic components and in production with model-backed embeddings and a persistent database.

## Available providers

| Capability | Built-in providers |
| --- | --- |
| Chat completion | OpenAI-compatible APIs, Anthropic Messages, Ollama |
| Embeddings | Deterministic local, OpenAI-compatible APIs, Ollama |
| Vector storage | In-memory and Qdrant in core; SQLite and PostgreSQL/pgvector in optional provider packages |
| Reranking | LLM, Cohere, ZeroEntropy, local cross-encoder |
| Entity and graph storage | In-memory, PostgreSQL |

All providers are replaceable through the public contracts. The model adapters use caller-owned `HttpClient` instances; custom providers can target other hosted services or local runtimes without adding an SDK dependency to the core package.

## Dependency boundary

The `Mem0Sharp` package has no runtime database dependencies. Install
`Mem0Sharp.PostgreSQL` for PostgreSQL/pgvector and relationship stores, or
`Mem0Sharp.SQLite` for the managed-cosine SQLite store. Each provider package
contains its own database dependencies; `InMemoryStore`,
`LocalEmbeddingGenerator`, the service API, and the provider interfaces use
.NET 10 and the base class libraries without external services.

```powershell
dotnet add package Mem0Sharp.PostgreSQL
dotnet add package Mem0Sharp.SQLite
```

`OpenAiCompatibleClient` uses the `HttpClient` provided by .NET. It does not
introduce an OpenAI SDK dependency, and it can be replaced with custom
implementations of `IEmbeddingGenerator` and `IChatCompletionClient` for
another endpoint or an offline deployment.

`AnthropicClient`, `OllamaClient`, and `QdrantMemoryStore` also use caller-owned `HttpClient` instances. Dispose those clients according to the lifetime chosen by the application; the providers do not dispose shared clients.

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

## Anthropic and Ollama

`AnthropicClient` implements Anthropic's native Messages protocol, including the separate system prompt and required API headers:

```csharp
var anthropic = new AnthropicClient(
    new HttpClient(),
    Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!,
    model: "claude-sonnet-4-5");

var memory = new MemoryService(extractor: new LlmMemoryExtractor(anthropic));
```

`OllamaClient` implements native `/api/chat` and `/api/embed` requests and supports batch embeddings without an API key:

```csharp
var ollama = new OllamaClient(
    new HttpClient(),
    chatModel: "llama3.2",
    embeddingModel: "nomic-embed-text",
    endpoint: new Uri("http://localhost:11434/"));

var memory = new MemoryService(
    embeddings: ollama,
    extractor: new LlmMemoryExtractor(ollama));
```

The Ollama embedding provider rejects missing batches, mismatched batch counts, and inconsistent vector dimensions before data reaches a vector store.

## Reranking providers

Set a reranker on `MemoryService` and enable `Rerank` for searches that should use it:

```csharp
using var rerankClient = new HttpClient();
var reranker = new CohereReranker(
    rerankClient,
    Environment.GetEnvironmentVariable("COHERE_API_KEY")!);

var memory = new MemoryService(reranker: reranker);
var results = await memory.SearchAsync("editor preferences", new MemorySearchOptions
{
    Rerank = true,
    Explain = true
});
```

`CohereReranker` uses Cohere's `v1/rerank` endpoint and defaults to `rerank-v3.5`. `ZeroEntropyReranker` uses `v1/models/rerank` and defaults to `zerank-1`. Both use bearer authentication, preserve the original memory by response index, clamp relevance scores to the range from 0 to 1, and expose the provider score through `SearchScoreDetails.Reranker`.

`LlmReranker` works with any `IChatCompletionClient`. For local Hugging Face, Sentence Transformers, ONNX, or another cross-encoder runtime, implement `ICrossEncoderScorer` and pass it to `CrossEncoderReranker`. Set `normalizeScores` to `true` for raw logits that need sigmoid normalization, or `false` for scores already normalized by the model runtime. This keeps heavyweight model dependencies outside the core package.

## PostgreSQL and pgvector

Install `Mem0Sharp.PostgreSQL` alongside `Mem0Sharp`:

```powershell
dotnet add package Mem0Sharp.PostgreSQL
```

`PostgresMemoryStore` persists memory fields and embeddings in PostgreSQL. Install PostgreSQL with the `vector` extension available, then initialize the store once before using it:

PostgreSQL and the `vector` extension are external infrastructure. They are
required only for the persistent PostgreSQL stores; the in-memory store does
not require a database server.

```csharp
using var httpClient = new HttpClient
{
    BaseAddress = new Uri("https://api.openai.com/")
};
var provider = new OpenAiCompatibleClient(
    httpClient,
    Environment.GetEnvironmentVariable("OPENAI_API_KEY")!);

await using var store = new PostgresMemoryStore(new PostgresMemoryStoreOptions
{
    ConnectionString = Environment.GetEnvironmentVariable("MEM0_POSTGRES")!,
    EmbeddingDimensions = 1536,
    TableName = "mem0_memories",
    UseHnswIndex = true,
    CreateExtension = true
});

await store.InitializeAsync();

var entityStore = new PostgresEntityStore(new PostgresMemoryStoreOptions
{
    ConnectionString = Environment.GetEnvironmentVariable("MEM0_POSTGRES")!,
    EmbeddingDimensions = 1536,
    TableName = "mem0_memories"
});
var graphStore = new PostgresGraphStore(new PostgresMemoryStoreOptions
{
    ConnectionString = Environment.GetEnvironmentVariable("MEM0_POSTGRES")!,
    EmbeddingDimensions = 1536,
    TableName = "mem0_memories"
});
await entityStore.InitializeAsync();
await graphStore.InitializeAsync();

var memory = new MemoryService(
    store: store,
    embeddings: provider,
    extractor: new LlmMemoryExtractor(provider),
    entityStore: entityStore,
    graphExtractor: new LlmGraphMemoryExtractor(provider),
    graphStore: graphStore);
```

See the runnable [PostgreSQL and OpenAI sample](../samples/PostgresOpenAI/README.md) for Docker setup, environment variables, and a complete search workflow.

`EmbeddingDimensions` must exactly match the number of values returned by the embedding provider. The default OpenAI `text-embedding-3-small` model returns 1536 dimensions.

Memory-store initialization creates the memory table, a `<TableName>_history` audit table, their indexes, and an HNSW cosine index when enabled and supported. History rows preserve the memory creation time separately from the event time, deletion state, actor ID, and role. Initialization upgrades older history tables with these fields and preserves existing events. The relationship stores create `<TableName>_entities` and `<TableName>_relations`. HNSW creation is skipped automatically when the configured dimension is greater than 2000. Set `UseHnswIndex` to `false` when an HNSW index is not wanted.

Set `CreateExtension = false` when the database user cannot create extensions and the `vector` extension has already been installed by an administrator.

The table name must be a simple PostgreSQL identifier containing letters, numbers, and underscores, and beginning with a letter or underscore.

## SQLite

Install `Mem0Sharp.SQLite` alongside `Mem0Sharp`:

```powershell
dotnet add package Mem0Sharp.SQLite
```

`SqliteMemoryStore` persists embeddings as portable BLOBs and implements cosine similarity in managed code. It requires no SQLite vector extension and is suitable for local applications and small to medium datasets where a scan of stored vectors is acceptable:

```csharp
await using var store = new SqliteMemoryStore("data/mem0sharp.db");
await store.InitializeAsync();

var memory = new MemoryService(
    store: store,
    embeddings: new LocalEmbeddingGenerator(384));
```

The SQLite memory and history tables use the same normalized column names as the PostgreSQL store, including `text_value`, `user_id`, `metadata`, `created_at`, `expires_at`, `hash_value`, `behavior`, `memory_type`, and the explicit history event fields. Metadata is stored as JSON text and embeddings are stored as portable BLOBs because SQLite has no required vector type; cosine similarity is evaluated in managed code. Initialization adds the provenance columns to older normalized Mem0Sharp tables with safe defaults; older JSON-backed schemas are not supported. For large collections or indexed approximate search, use PostgreSQL/pgvector, Qdrant, or a custom `IVectorMemoryStore` backed by a SQLite vector extension.

## Qdrant

`QdrantMemoryStore` provides persistent vector storage through Qdrant's REST API:

```csharp
using var qdrantClient = new HttpClient();
var store = new QdrantMemoryStore(qdrantClient, new QdrantMemoryStoreOptions
{
    Endpoint = new Uri("http://localhost:6333/"),
    CollectionName = "mem0_memories",
    EmbeddingDimensions = 384,
    ApiKey = Environment.GetEnvironmentVariable("QDRANT_API_KEY")
});
await store.InitializeAsync();

var memory = new MemoryService(store, new LocalEmbeddingGenerator(384));
```

Qdrant stores memory payloads and vectors remotely. Mem0Sharp pages the persisted points and applies the same nested filter evaluator and cosine scoring used by the local path, preserving all filter operators and deterministic semantics. This favors behavioral parity over server-side approximate-query throughput; custom `IVectorMemoryStore` implementations can use native Qdrant query filters when workload-specific optimization is required.

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

Implement `IMemoryStore` for CRUD storage. Add `IVectorMemoryStore` when the store can perform similarity search itself; otherwise `MemoryService` uses its local vector cache and scans up to `MaxCandidateCount` memories. Add `IBulkMemoryStore` when filtered deletion can be performed efficiently by the backend. Add `IMemoryHistoryStore` to retain `ADD`, `UPDATE`, and `DELETE` events and support `GetHistoryAsync`. Add `IAtomicMemoryStore` when the backend can commit memory rows and history events in one transaction; this is the consistency boundary used by the built-in stores.

All custom implementations should honor cancellation tokens and return vectors with a stable dimension.

These providers are native C# persistence components. They do not call Mem0 Platform. `OpenAiCompatibleClient` is optional and only supplies model inference; use `LocalEmbeddingGenerator`, `BasicMemoryExtractor`, and custom local provider implementations to keep all inference offline.
