# Mem0Sharp

Long-term memory for AI applications in .NET 10. Mem0Sharp is an independent C#/.NET implementation of the open-source [Mem0 project](https://github.com/mem0ai/mem0), with one service API for saving, searching, updating, and deleting semantic memories while keeping embedding and storage providers replaceable.

Mem0Sharp is not affiliated with, sponsored by, or endorsed by Mem0 or mem0ai.

## Documentation

- [Getting started](docs/getting-started.md) - install, create a service, and use the core API.
- [Providers and persistence](docs/providers-and-persistence.md) - configure OpenAI-compatible embeddings, LLM extraction, and PostgreSQL with pgvector.
- [API reference](docs/api-reference.md) - understand models, filters, scopes, options, and extension points.

## Features

- Semantic memory search with configurable result limits.
- CRUD operations plus filtered bulk deletion.
- User, session, and agent scopes with user, agent, and run filters.
- Metadata attached to each memory.
- Zero-dependency in-memory storage for tests and local development.
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

The provider boundary also works with compatible local servers. See [Providers and persistence](docs/providers-and-persistence.md) for model selection, embedding dimensions, and initialization details.

## Install from GitHub Packages

GitHub Packages requires authentication for NuGet clients. Create a GitHub personal access token with `read:packages`, then add the package source:

```powershell
dotnet nuget add source `
	--username YOUR_GITHUB_USERNAME `
	--password YOUR_GITHUB_TOKEN `
	--store-password-in-clear-text `
	--name github `
	https://nuget.pkg.github.com/jihadkhawaja/index.json
```

Install the package from the repository's default branch package feed:

```powershell
dotnet add package Mem0Sharp --version 0.1.0 --source github
```

Once published to NuGet.org, it can be installed without a custom package source:

```powershell
dotnet add package Mem0Sharp --version 0.1.0
```

To publish a new version, push a version tag such as `v0.1.0`. The GitHub Actions workflow builds and tests the library, then publishes the package to GitHub Packages and NuGet.org using trusted publishing. Package versions must be unique. The NuGet.org workflow uses GitHub's short-lived OIDC credentials and does not store a NuGet API key.

## Build and test

```powershell
dotnet build .\src\Mem0Sharp\Mem0Sharp.csproj
dotnet test .\tests\Mem0Sharp.Tests\Mem0Sharp.Tests.csproj
```

