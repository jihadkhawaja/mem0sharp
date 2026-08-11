# Mem0Sharp.PostgreSQL

PostgreSQL and pgvector persistence providers for [Mem0Sharp](https://github.com/jihadkhawaja/mem0sharp).

## Install

```powershell
dotnet add package Mem0Sharp
dotnet add package Mem0Sharp.PostgreSQL
```

The package provides `PostgresMemoryStore`, `PostgresMemoryStoreOptions`,
`PostgresEntityStore`, and `PostgresGraphStore` in the `Mem0Sharp` namespace.
PostgreSQL must have the `vector` extension available.

```csharp
using Mem0Sharp;

await using var store = new PostgresMemoryStore(new PostgresMemoryStoreOptions
{
    ConnectionString = connectionString,
    EmbeddingDimensions = 1536,
    TableName = "mem0_memories"
});
await store.InitializeAsync();

var memory = new MemoryService(store, embeddings);
```

The provider package follows the same version as `Mem0Sharp`.
