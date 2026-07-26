# API reference

## Runtime requirements

Mem0Sharp targets .NET 10 and has one direct runtime NuGet dependency:
`Npgsql`, used by the PostgreSQL and pgvector stores. The core service,
in-memory store, deterministic local embeddings, and extension-point
interfaces do not require additional third-party packages. Model providers
and persistence backends are replaceable through the interfaces below.

## MemoryService

`MemoryService` implements `IMemoryService` and is the main application entry point.

| Method | Purpose |
| --- | --- |
| `AddAsync(string, ...)` | Save one memory and generate its embedding. |
| `AddAsync(IEnumerable<Message>, ...)` | Extract and save memories from conversation messages. |
| `AddManyAsync(IEnumerable<string>, ...)` | Deduplicate, batch embed, and save several memories. |
| `SearchAsync(string, ...)` | Return the most relevant memories for a query. |
| `SearchManyAsync(IEnumerable<string>, ...)` | Search several queries with the same filter. |
| `GetAsync(string)` | Retrieve one memory by ID. |
| `GetAllAsync(MemoryFilter?)` | List memories, newest updated first. |
| `GetPageAsync(MemoryPageOptions, MemoryFilter?)` | Return a page plus the total matching count. |
| `UpdateAsync(string, string, ...)` | Replace text and optionally metadata, then regenerate its embedding. |
| `DeleteAsync(string)` | Delete one memory by ID. |
| `DeleteAllAsync(MemoryFilter?)` | Delete all matching memories and return the count. |
| `GetHistoryAsync(string)` | Return chronological `ADD`, `UPDATE`, and `DELETE` events for one memory. |
| `GetRelationsAsync(string?)` | Return graph relations when a graph store is configured. |
| `ResetAsync()` | Clear memory, history, vector cache, entities, and graph state. |

All methods are asynchronous and accept an optional `CancellationToken`.

## Models

- `Memory` is the stored record. It contains `Id`, `Text`, `UserId`, optional `AgentId` and `RunId`, `Scope`, `Metadata`, `CreatedAt`, and `UpdatedAt`.
- `MemoryInput` is the extractor output used when creating memories.
- `Message` contains a conversation `Role` and `Content`.
- `SearchResult` contains a `Memory` and its similarity `Score`.
- `AddResult` contains the memories created by an add operation.
- `MemoryHistoryEntry` contains the event type, old and new text, memory ID, event ID, and timestamp.
- `MemoryAddOptions` controls identity, scope, inference, procedural memory, expiration, metadata, custom prompts, and deduplication.
- `MemorySearchOptions` controls filtering, top K, threshold, hybrid scoring, explanations, and reranking.
- `MemoryUpdate` supports optional text, metadata, and expiration changes.
- `MemoryPage` contains paged results and total count.
- `SearchScoreDetails` separates semantic, keyword, entity/graph, and reranker signals.

## Filters and scopes

`MemoryFilter` can constrain reads, searches, and deletion by any combination of:

```csharp
var filter = new MemoryFilter(
    UserId: "alice",
    AgentId: "support-agent",
    RunId: "conversation-42",
    Scope: MemoryScope.Session);
```

`MemoryScope` has three values:

- `User` for facts associated with a user.
- `Session` for short-lived conversation or session context.
- `Agent` for facts associated with an agent.

The scope is metadata used for filtering; it does not automatically expire memories.

## Tuning search

`MemoryOptions` controls the service defaults:

- `DefaultTopK` is used when a search does not provide `topK`.
- `MinimumScore` filters results when the service scans a non-vector store.
- `MaxCandidateCount` bounds that scan for non-vector stores.

A vector store such as `PostgresMemoryStore` applies similarity ordering and `topK` in the backend.

## Extension points

- `IEmbeddingGenerator` generates a vector for text.
- `IMemoryExtractor` converts messages into `MemoryInput` values.
- `IMemoryStore` provides basic persistence operations.
- `IVectorMemoryStore` adds backend similarity search.
- `IBulkMemoryStore` adds efficient filtered bulk deletion.
- `IMemoryHistoryStore` persists and retrieves the audit trail used by `GetHistoryAsync`.
- `IBatchEmbeddingGenerator`, `IBatchMemoryStore`, and `IBatchVectorMemoryStore` enable batch pipelines.
- `IMemoryConflictResolver` produces structured memory actions.
- `IEntityExtractor`/`IEntityStore` and `IGraphMemoryExtractor`/`IGraphMemoryStore` provide relationship memory.
- `IMemoryReranker` reranks fused search candidates.
- `IMemoryTelemetry` receives privacy-preserving operation events when configured.

`MemoryServiceConfiguration` composes these providers without any hosted Mem0 dependency. `SynchronousMemoryService` exposes blocking equivalents for applications that cannot use async APIs. `MemoryMcpServer` exposes local JSON-RPC tools over `IMemoryService`.

The service only requires `IMemoryStore`. If the supplied store does not implement `IVectorMemoryStore`, it falls back to generating and caching vectors in the service process. If it does not implement `IMemoryHistoryStore`, no history events are recorded and `GetHistoryAsync` returns an empty list. These fallbacks are suitable for local development and small datasets; use vector- and history-capable persistent stores for production workloads.

## Operational notes

- Use a stable `UserId` for each user so filters isolate data correctly.
- Keep embedding dimensions aligned between the configured provider and PostgreSQL.
- Treat `InMemoryStore` as ephemeral; all data is lost when the process exits.
- `OpenAiCompatibleClient` expects the provider root as `BaseAddress`, not the `/v1` path, because it appends `/v1/embeddings` and `/v1/chat/completions` itself.
