# mem0-python parity

Mem0Sharp is an independent, standalone implementation of Mem0's open-source behavior for .NET. It does not call the hosted Mem0 Platform API and does not depend on mem0.ai at runtime. This ledger tracks behavioral parity with the self-hosted Python SDK. A row is complete only when its public API, persistence semantics, tests, and documentation are present.

Status meanings:

- **Complete**: supported by the built-in local and PostgreSQL paths and covered by tests.
- **Partial**: a useful subset exists, but Python behavior or provider breadth is missing.
- **Missing**: no supported public implementation exists yet.

## Core memory API

| Python capability | Status | Mem0Sharp state |
| --- | --- | --- |
| Add raw text | Complete | `AddAsync(string)` |
| Add conversation messages | Complete | Raw and inferred adds, custom prompts, conflict actions, procedural memory, expiration, metadata, and scopes are supported. |
| Get one / list | Complete | CRUD, paging, expiration visibility, and expression filters are supported. |
| Update | Complete | Text, metadata, expiration, hash regeneration, entity relinking, graph relinking, and history are supported. |
| Delete / filtered delete all | Complete | Single and filtered bulk deletion are supported. |
| History | Partial | Built-in stores persist chronological Add, Update, and Delete events; PostgreSQL integration coverage is still needed. |
| Reset | Complete | `ResetAsync` clears memories, history, vectors, entities, and graph state. |
| Sync API | Complete | `SynchronousMemoryService` wraps the native async API. |

## Retrieval

| Python capability | Status | Mem0Sharp state |
| --- | --- | --- |
| Semantic vector search | Complete | In-process cosine fallback and PostgreSQL pgvector search exist. |
| Batch search | Complete | `SearchManyAsync` |
| Score threshold | Complete | Semantic thresholds are applied consistently after backend retrieval. |
| Metadata filters and operators | Complete | Nested And/Or/Not plus equality, ranges, membership, containment, and existence are supported. |
| BM25 keyword search | Complete | Built-in BM25 scoring is normalized and fused with semantic retrieval. |
| Entity extraction and boosting | Complete | Built-in extraction, linking, cleanup, boosts, and in-memory/PostgreSQL stores exist. |
| Reranking | Partial | `IMemoryReranker` and `LlmReranker` exist; vendor-specific rerankers are not bundled. |
| Search explanations | Complete | Semantic, keyword, entity/graph, raw, maximum, threshold, and reranker scores are exposed. |

## Memory intelligence

| Python capability | Status | Mem0Sharp state |
| --- | --- | --- |
| LLM fact extraction | Complete | Extraction and structured Add/Update/Delete/None conflict decisions are provider-based. |
| Deduplication | Complete | Scope-aware SHA-256 content hashes suppress stored and within-batch duplicates. |
| Entity graph/linking | Complete | Native extraction, linking, boosts, cleanup, and in-memory/PostgreSQL stores exist. |
| Graph memory | Complete | Native graph contracts, LLM triple extraction, in-memory/PostgreSQL storage, boosts, and lifecycle cleanup exist. |
| Procedural memory | Complete | Agent-scoped procedure generation is supported through `IProceduralMemoryGenerator`. |
| Expiration | Complete | Expiration persistence and default expired-memory filtering are supported. |

## Providers

| Python capability | Status | Mem0Sharp state |
| --- | --- | --- |
| LLM providers | Partial | OpenAI-compatible endpoints only. |
| Embedding providers | Partial | Deterministic local and OpenAI-compatible embeddings. |
| Vector stores | Partial | In-memory and PostgreSQL/pgvector. |
| Rerank providers | Partial | LLM reranking is included behind `IMemoryReranker`. |
| Graph providers | Complete | In-memory and PostgreSQL graph stores are included behind `IGraphMemoryStore`. |

## Operations

| Python capability | Status | Mem0Sharp state |
| --- | --- | --- |
| Telemetry | Complete | Opt-in `IMemoryTelemetry` captures content-free operation events. |
| MCP integration | Complete | `MemoryMcpServer` exposes nine local JSON-RPC tools and a stream transport. |
| Configuration model | Complete | `MemoryServiceConfiguration` composes all native C# providers. |
| Batch embedding/insertion | Complete | Batch contracts, OpenAI-compatible batch embeddings, local batching, and transactional PostgreSQL batch writes are supported. |

## Implementation order

Remaining parity work is provider breadth and integration coverage:

1. Add vendor-specific LLM, embedding, reranker, and vector-store packages without changing the core API.
2. Run PostgreSQL integration tests against a real pgvector service in CI.

The hosted Mem0 Platform client is intentionally out of scope. Adding it would turn Mem0Sharp into a wrapper around mem0.ai instead of a standalone C# reimplementation.

Provider count alone does not establish parity. Each provider must pass the same behavioral contract for cancellation, filtering, dimensions, persistence, errors, and disposal.
