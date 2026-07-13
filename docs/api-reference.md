# API reference

## MemoryService

`MemoryService` implements `IMemoryService` and is the main application entry point.

| Method | Purpose |
| --- | --- |
| `AddAsync(string, ...)` | Save one memory and generate its embedding. |
| `AddAsync(IEnumerable<Message>, ...)` | Extract and save memories from conversation messages. |
| `SearchAsync(string, ...)` | Return the most relevant memories for a query. |
| `SearchManyAsync(IEnumerable<string>, ...)` | Search several queries with the same filter. |
| `GetAsync(string)` | Retrieve one memory by ID. |
| `GetAllAsync(MemoryFilter?)` | List memories, newest updated first. |
| `UpdateAsync(string, string, ...)` | Replace text and optionally metadata, then regenerate its embedding. |
| `DeleteAsync(string)` | Delete one memory by ID. |
| `DeleteAllAsync(MemoryFilter?)` | Delete all matching memories and return the count. |

All methods are asynchronous and accept an optional `CancellationToken`.

## Models

- `Memory` is the stored record. It contains `Id`, `Text`, `UserId`, optional `AgentId` and `RunId`, `Scope`, `Metadata`, `CreatedAt`, and `UpdatedAt`.
- `MemoryInput` is the extractor output used when creating memories.
- `Message` contains a conversation `Role` and `Content`.
- `SearchResult` contains a `Memory` and its similarity `Score`.
- `AddResult` contains the memories created by an add operation.

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

The service only requires `IMemoryStore`. If the supplied store does not implement `IVectorMemoryStore`, it falls back to generating and caching vectors in the service process. This fallback is suitable for local development and small datasets; use a vector-capable persistent store for production workloads.

## Operational notes

- Use a stable `UserId` for each user so filters isolate data correctly.
- Keep embedding dimensions aligned between the configured provider and PostgreSQL.
- Treat `InMemoryStore` as ephemeral; all data is lost when the process exits.
- `OpenAiCompatibleClient` expects the provider root as `BaseAddress`, not the `/v1` path, because it appends `/v1/embeddings` and `/v1/chat/completions` itself.
