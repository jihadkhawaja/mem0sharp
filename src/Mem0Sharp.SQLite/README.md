# Mem0Sharp.SQLite

SQLite persistence provider for [Mem0Sharp](https://github.com/jihadkhawaja/mem0sharp).

## Install

```powershell
dotnet add package Mem0Sharp
dotnet add package Mem0Sharp.SQLite
```

The package provides `SqliteMemoryStore` in the `Mem0Sharp` namespace. It stores
embeddings as portable blobs and evaluates cosine similarity in managed code;
no SQLite vector extension is required.

```csharp
using Mem0Sharp;

await using var store = new SqliteMemoryStore("data/mem0sharp.db");
await store.InitializeAsync();

var memory = new MemoryService(store, new LocalEmbeddingGenerator(384));
```

The provider package follows the same version as `Mem0Sharp`.
