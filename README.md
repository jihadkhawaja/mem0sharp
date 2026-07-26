# Mem0Sharp

[![NuGet version](https://img.shields.io/nuget/v/Mem0Sharp.svg)](https://www.nuget.org/packages/Mem0Sharp)

Long-term memory for AI applications in .NET 10. Mem0Sharp is an independent, standalone C#/.NET implementation of the open-source [Mem0 project](https://github.com/mem0ai/mem0), with one service API for saving, searching, updating, and deleting semantic memories while keeping embedding and storage providers replaceable. It does not call the hosted Mem0 Platform API or depend on mem0.ai at runtime.

Mem0Sharp is not affiliated with, sponsored by, or endorsed by Mem0 or mem0ai.

## Attribution and trademarks

Mem0Sharp is an independent .NET implementation inspired by the open-source
[Mem0 project](https://github.com/mem0ai/mem0). The original Mem0 project is
copyright 2023 Taranjeet Singh and is licensed under the Apache License 2.0.
Copyright for the Mem0Sharp implementation and its modifications is held by
Jihad Khawaja and contributors. See [NOTICE](NOTICE) and [LICENSE](LICENSE)
for the complete attribution and license terms.

Mem0 and related names, logos, and marks belong to their respective owners.
This project does not grant or claim any trademark rights and is not affiliated
with, sponsored by, or endorsed by the Mem0 project or mem0ai.

## Documentation

- [Getting started](docs/getting-started.md) - install, create a service, and use the core API.
- [Architecture](docs/architecture.md) - understand dependency direction, source boundaries, and extension rules.
- [Providers and persistence](docs/providers-and-persistence.md) - configure OpenAI-compatible embeddings, LLM extraction, and PostgreSQL with pgvector.
- [API reference](docs/api-reference.md) - understand models, filters, scopes, options, and extension points.
- [Mem0 python feature parity](docs/mem0-python-parity.md) - track implemented, partial, and missing Mem0 capabilities.

## Dependency stack

Mem0Sharp keeps its runtime dependency stack deliberately small: it has one
direct NuGet dependency, [Npgsql](https://www.nuget.org/packages/Npgsql), for
the PostgreSQL and pgvector stores. The default in-memory service and the
provider interfaces use only .NET 10 and the base class libraries; no AI SDK,
HTTP client package, ORM, or vector database package is required.

Model access is provider-based. `OpenAiCompatibleClient` uses the .NET
`HttpClient` and does not add another package. Replace it with implementations
of `IChatCompletionClient` and `IEmbeddingGenerator` when using a different
model service or a fully offline deployment. PostgreSQL itself and its
`vector` extension are infrastructure prerequisites, not NuGet dependencies.

## Features

- Semantic memory search with configurable result limits.
- Hybrid semantic and BM25 retrieval with explanations and optional reranking.
- CRUD operations plus filtered bulk deletion.
- Persistent `ADD`, `UPDATE`, and `DELETE` history for built-in stores.
- Scope-aware deduplication and conflict-aware Add/Update/Delete/None decisions.
- Expiration, paging, nested metadata filters, entities, and optional graph memory.
- Batch embedding and transactional PostgreSQL batch persistence.
- Native synchronous facade, opt-in telemetry, and nine local MCP tools.
- User, session, and agent scopes with user, agent, and run filters.
- Metadata attached to each memory.
- Zero-dependency in-memory storage for tests and local development.
- One direct runtime package dependency: `Npgsql`; all other provider boundaries are native .NET abstractions.
- Deterministic local embeddings for offline development.
- OpenAI-compatible chat completion and embedding support.
- PostgreSQL persistence with pgvector and optional HNSW indexing.

## Quick start

The default service uses `InMemoryStore`, `LocalEmbeddingGenerator`, and `BasicMemoryExtractor`. It is a good starting point for development and tests; use a persistent store in production.

```csharp
using Mem0Sharp;

var memory = new MemoryService();
await memory.AddAsync("I prefer dark mode and vim keybindings", userId: "alice");

var results = await memory.SearchAsync(
	"What editor settings does Alice prefer?",
	new MemoryFilter(UserId: "alice"),
	topK: 3);

foreach (var result in results)
{
    Console.WriteLine($"{result.Score:F3}: {result.Memory.Text}");
}

var allAliceMemories = await memory.GetAllAsync(new MemoryFilter(UserId: "alice"));
var memoryId = allAliceMemories[0].Id;
await memory.UpdateAsync(memoryId, "I prefer dark mode and Vim keybindings");
await memory.DeleteAsync(memoryId);

var history = await memory.GetHistoryAsync(memoryId);
```

To add memories extracted from a conversation, pass `Message` values. The default extractor stores non-empty message content; the LLM-backed extractor is shown in the provider guide.

```csharp
await memory.AddAsync(
[
    new Message("user", "I live in Berlin."),
    new Message("assistant", "Thanks, I will remember that.")
],
userId: "alice",
scope: MemoryScope.User);
```

## OpenAI-compatible provider

Pass an `HttpClient` whose `BaseAddress` points at the provider root, such as `https://api.openai.com/`:

```csharp
var provider = new OpenAiCompatibleClient(httpClient, Environment.GetEnvironmentVariable("OPENAI_API_KEY")!);
var memory = new MemoryService(
	embeddings: provider,
	extractor: new LlmMemoryExtractor(provider));
```

## PostgreSQL with pgvector

Install PostgreSQL with the `vector` extension, then initialize a store using the same embedding dimension as the configured embedding model:

```csharp
var store = new PostgresMemoryStore(new PostgresMemoryStoreOptions
{
	ConnectionString = Environment.GetEnvironmentVariable("MEM0_POSTGRES")!,
	EmbeddingDimensions = 1536,
	TableName = "mem0_memories"
});
await store.InitializeAsync();

var memory = new MemoryService(store, provider, new LlmMemoryExtractor(provider));
```

The store persists memory metadata and embeddings, applies user/agent/run/scope filters in SQL, uses cosine distance for vector search, and creates an HNSW index when the embedding dimension is supported by pgvector. `CreateExtension = false` can be used when the database user cannot create extensions.

`MemoryService` also provides `SearchManyAsync` for batch queries and `DeleteAllAsync` for filtered bulk deletion.

Mem0Sharp never sends memories to a Mem0 or mem0.ai backend. `OpenAiCompatibleClient` is an optional model provider for extraction and embeddings; replace it with local implementations of `IChatCompletionClient` and `IEmbeddingGenerator` for a fully offline deployment.

The provider boundary also works with compatible local servers. See [Providers and persistence](docs/providers-and-persistence.md) for model selection, embedding dimensions, and initialization details.

## Install from NuGet.org

```powershell
dotnet add package Mem0Sharp
```

This installs Mem0Sharp and its single direct runtime dependency, `Npgsql`.
You only need a PostgreSQL server with the `vector` extension when using the
persistent stores; the default in-memory configuration has no external service
requirement.

## Build and test

```powershell
dotnet build .\src\Mem0Sharp\Mem0Sharp.csproj
dotnet test .\tests\Mem0Sharp.Tests\Mem0Sharp.Tests.csproj
```
