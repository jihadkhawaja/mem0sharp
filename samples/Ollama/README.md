# Ollama sample

This sample runs model-backed memory extraction and embeddings locally through [OllamaSharp](https://github.com/awaescher/OllamaSharp) using standard `Microsoft.Extensions.AI` abstractions (`IChatClient` and `IEmbeddingGenerator`). Memory storage remains in process.

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

The application uses `OllamaApiClient.AsChatClient("llama3.2")` and `OllamaApiClient.AsEmbeddingGenerator("nomic-embed-text")` to extract facts from a conversation, generate vector embeddings, store them in memory, and execute semantic recall.
