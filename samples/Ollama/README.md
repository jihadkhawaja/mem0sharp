# Ollama sample

This sample runs model-backed memory extraction and embeddings locally through Ollama. Memory storage remains in process.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Ollama](https://ollama.com/)
- The chat and embedding models used by the sample

```powershell
ollama pull llama3.2
ollama pull nomic-embed-text
```

Ensure Ollama is running at `http://localhost:11434`.

## Run it

From the repository root:

```powershell
dotnet run --project .\samples\Ollama\Ollama.csproj
```

The application extracts facts from a short conversation, stores their Ollama embeddings in memory, and searches within Alice's user scope.

Change the model names or endpoint in [Program.cs](Program.cs) when your Ollama installation uses different values. For durable storage, continue with the [PostgreSQL and OpenAI sample](../PostgresOpenAI/README.md).
