# Getting started

Mem0Sharp targets .NET 10 and exposes the `MemoryService` API for long-term application memory.

## Install

Reference the project during development:

```powershell
dotnet add .\src\YourApp\YourApp.csproj reference .\src\Mem0Sharp\Mem0Sharp.csproj
```

The package also includes the PostgreSQL integration and its Npgsql dependency. Build the library with:

```powershell
dotnet build .\src\Mem0Sharp\Mem0Sharp.csproj
```

## Create a service

The parameterless constructor is deliberately useful for tests and offline development. It selects:

- `InMemoryStore` for storage.
- `LocalEmbeddingGenerator` for deterministic, local embeddings.
- `BasicMemoryExtractor` for conversation messages.

```csharp
using Mem0Sharp;

var memory = new MemoryService();
```

## Save and search

A memory belongs to a user by default. Optional agent, run, scope, and metadata values can be supplied when saving it.

```csharp
var added = await memory.AddAsync(
    "I prefer dark mode and Vim keybindings",
    userId: "alice",
    metadata: new Dictionary<string, string>
    {
        ["source"] = "settings"
    });

var results = await memory.SearchAsync(
    "Which editor settings does Alice prefer?",
    new MemoryFilter(UserId: "alice"),
    topK: 5);

foreach (var result in results)
{
    Console.WriteLine($"{result.Score:F3}: {result.Memory.Text}");
}
```

`SearchResult.Score` is a cosine-similarity score. Results are ordered from the highest score to the lowest score. The in-memory fallback excludes results below `MemoryOptions.MinimumScore`.

## Store conversation memories

Pass messages when the memory should be extracted from a conversation. The default extractor turns each non-empty message into a memory and stores its role in metadata.

```csharp
await memory.AddAsync(
[
    new Message("user", "I live in Berlin."),
    new Message("assistant", "I will remember that.")
],
userId: "alice",
scope: MemoryScope.User);
```

For model-backed fact extraction, use `LlmMemoryExtractor` with an OpenAI-compatible client as described in [Providers and persistence](providers-and-persistence.md).

## Read, update, and delete

```csharp
var memories = await memory.GetAllAsync(new MemoryFilter(UserId: "alice"));
var id = memories[0].Id;

var current = await memory.GetAsync(id);
var updated = await memory.UpdateAsync(id, "I prefer dark mode and Vim keybindings");

await memory.DeleteAsync(id);
var removed = await memory.DeleteAllAsync(new MemoryFilter(UserId: "alice"));
```

`UpdateAsync` regenerates the embedding. `DeleteAllAsync` returns the number of deleted memories and applies the same filter fields as search and listing.

## Configure defaults

```csharp
var memory = new MemoryService(
    options: new MemoryOptions
    {
        DefaultTopK = 10,
        MinimumScore = 0.15,
        MaxCandidateCount = 500
    });
```

`MaxCandidateCount` limits how many memories the non-vector fallback examines. Vector stores apply `topK` in the database.

## Next steps

- Use [Providers and persistence](providers-and-persistence.md) for model-backed embeddings and PostgreSQL.
- Use [API reference](api-reference.md) for interfaces, filters, scopes, and custom implementations.
