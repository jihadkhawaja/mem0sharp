# Mem0Sharp samples

These runnable projects progress from a zero-dependency local setup to model-backed extraction and durable vector storage.

| Sample | What it demonstrates | Requirements |
| --- | --- | --- |
| [Getting started](GettingStarted/README.md) | Add, search, update, history, and delete | .NET 10 |
| [Memory behaviors](MemoryBehaviors/README.md) | Normal, dreaming, random-thought, and personality-shaped memory | .NET 10, OpenAI API key |
| [Ollama](Ollama/README.md) | Local model-backed extraction and embeddings | .NET 10, Ollama |
| [PostgreSQL and OpenAI](PostgresOpenAI/README.md) | OpenAI-compatible models with PostgreSQL/pgvector persistence | .NET 10, Docker, API key |

Run a sample from the repository root:

```powershell
dotnet run --project .\samples\GettingStarted\GettingStarted.csproj
```

Each sample references the local Mem0Sharp project so it is compiled against the current source. In your own application, install the package instead:

```powershell
dotnet add package Mem0Sharp
```
