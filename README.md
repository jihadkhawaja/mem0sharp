# Mem0Sharp

[![NuGet version](https://img.shields.io/nuget/v/Mem0Sharp.svg)](https://www.nuget.org/packages/Mem0Sharp)

Long-term memory for AI applications in .NET 10. Mem0Sharp is an independent, standalone C#/.NET implementation of the open-source [Mem0 project](https://github.com/mem0ai/mem0), with one service API for saving, searching, updating, and deleting semantic memories while keeping embedding and storage providers replaceable. It does not call the hosted Mem0 Platform API or depend on mem0.ai at runtime.

Mem0Sharp is not affiliated with, sponsored by, or endorsed by Mem0 or mem0ai.

Mem0Sharp goes beyond porting Mem0 to .NET. It adds native capabilities such as **memory behaviors**, letting agents remember normally, consolidate experiences like dreams, surface spontaneous associations, or shape first-person memories through their own identity and personality.

## Install

```powershell
dotnet add package Mem0Sharp
```

Mem0Sharp targets .NET 10 and is the dependency-free core package. The
default in-memory configuration does not require a database or any external
service. Install the optional provider packages when persistence is needed:

```powershell
dotnet add package Mem0Sharp.PostgreSQL
dotnet add package Mem0Sharp.SQLite
```

## Documentation

- **Start:** [Documentation home](docs/README.md), [getting started](docs/getting-started.md), and [runnable samples](samples/README.md).
- **Deploy:** [Providers and persistence](docs/providers-and-persistence.md) for OpenAI-compatible, Anthropic, and Ollama models plus PostgreSQL, Qdrant, and reranking providers.
- **Reference:** [API reference](docs/api-reference.md) and [Mem0 Python feature parity](docs/mem0-python-parity.md).
- **Evaluate:** [Evaluation](docs/evaluation.md) for the benchmark harness and measured results across options and memory behaviors.
- **Contribute:** [Architecture](docs/architecture.md) and the [contribution guide](CONTRIBUTING.md).

## Dependency stack

Mem0Sharp keeps the core runtime dependency-free. PostgreSQL and SQLite
dependencies live in the optional
[`Mem0Sharp.PostgreSQL`](https://www.nuget.org/packages/Mem0Sharp.PostgreSQL) and
[`Mem0Sharp.SQLite`](https://www.nuget.org/packages/Mem0Sharp.SQLite) packages.
The core service, in-memory store, local embeddings, and provider interfaces
use only .NET 10 and the base class libraries; no AI SDK, HTTP client package,
ORM, or vector database package is required.

Model access is provider-based. Implement `IChatCompletionClient` and
`IEmbeddingGenerator` for the model service used by your application, or use
the built-in local components for offline development. PostgreSQL itself and
its `vector` extension are infrastructure prerequisites, not NuGet dependencies.

## Features

- Semantic memory search with configurable result limits.
- Hybrid semantic and BM25 retrieval with explanations and LLM, Cohere, ZeroEntropy, or local cross-encoder reranking.
- OpenAI-compatible, Anthropic, and Ollama model protocols with local and Qdrant vector storage in core, plus optional SQLite and PostgreSQL/pgvector provider packages.
- CRUD operations plus filtered bulk deletion.
- Persistent `ADD`, `UPDATE`, and `DELETE` history with audit timestamps, deletion state, actor, and role for built-in stores.
- Scope-aware deduplication and conflict-aware Add/Update/Delete/None decisions.
- Memory behaviors beyond traditional fact storage: normal, dreaming, random thoughts, and personality-shaped first-person memory.
- Expiration, paging, nested metadata filters, entities, and optional graph memory.
- Batch embedding and transactional PostgreSQL batch persistence.
- Native synchronous facade, opt-in telemetry, and nine local MCP tools.
- User, session, and agent scopes with user, agent, and run filters.
- Metadata attached to each memory.
- Zero-dependency in-memory storage for tests and local development.
- Dependency-free core package with storage providers distributed separately.
- Deterministic local embeddings for offline development.
- Optional PostgreSQL persistence with pgvector and HNSW indexing through `Mem0Sharp.PostgreSQL`.

## Quick start

The default service uses in-memory storage, deterministic lexical hashing embeddings, and basic message extraction. It is a good starting point for development and tests; use model-backed embeddings and persistent storage for production workloads.

Memories retain their originating `MemoryBehavior` and optional `MemoryType`.
Ordinary searches return factual `Normal` memories by default. Set
`IncludeNonFactual = true` or select a behavior explicitly when an application
intends to retrieve dreams, associations, personal memories, or procedures.

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

Run this complete workflow from the repository with:

```powershell
dotnet run --project .\samples\GettingStarted\GettingStarted.csproj
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

## PostgreSQL with pgvector

Install the PostgreSQL provider alongside the core package:

```powershell
dotnet add package Mem0Sharp.PostgreSQL
```

Install PostgreSQL with the `vector` extension and set `MEM0_POSTGRES` to a valid connection string. Then initialize a store using the same embedding dimension as your configured `IEmbeddingGenerator`:

```csharp
using Mem0Sharp;

var embeddings = new LocalEmbeddingGenerator(384);
await using var store = new PostgresMemoryStore(new PostgresMemoryStoreOptions
{
	ConnectionString = Environment.GetEnvironmentVariable("MEM0_POSTGRES")!,
	EmbeddingDimensions = 384,
	TableName = "mem0_memories"
});
await store.InitializeAsync();

var memory = new MemoryService(store, embeddings);
```

The store persists memory metadata and embeddings, applies user/agent/run/scope filters in SQL, uses cosine distance for vector search, and creates an HNSW index when the embedding dimension is supported by pgvector. `CreateExtension = false` can be used when the database user cannot create extensions.

`MemoryService` also provides `SearchManyAsync` for batch queries, using batch-capable embedding and vector-store providers when available, and `DeleteAllAsync` for filtered bulk deletion.

Mem0Sharp never sends memories to a Mem0 or mem0.ai backend. Supply your own
implementations of `IChatCompletionClient` and `IEmbeddingGenerator` when you
need model-backed extraction or embeddings. Use the built-in local components
for a fully offline deployment. See [Providers and persistence](docs/providers-and-persistence.md)
for provider contracts, embedding dimensions, and initialization details.

## SQLite

Install the SQLite provider for a portable local database without a vector
extension:

```powershell
dotnet add package Mem0Sharp.SQLite
```

```csharp
await using var store = new SqliteMemoryStore("data/mem0sharp.db");
await store.InitializeAsync();
var memory = new MemoryService(store, new LocalEmbeddingGenerator(384));
```

## Samples

- [Getting started](samples/GettingStarted/README.md) - zero-setup CRUD, search, and history.
- [Memory behaviors](samples/MemoryBehaviors/README.md) - compare conventional fact extraction with dreaming, random thoughts, and agent-personal memory.
- [Ollama](samples/Ollama/README.md) - local model-backed extraction and embeddings.
- [PostgreSQL and OpenAI](samples/PostgresOpenAI/README.md) - persistent pgvector storage with OpenAI-compatible models.

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

## Build and test

```powershell
dotnet build .\Mem0Sharp.slnx
dotnet test .\tests\Mem0Sharp.Tests\Mem0Sharp.Tests.csproj
```
