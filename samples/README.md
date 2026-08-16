# Mem0Sharp samples

These runnable projects progress from a zero-dependency local setup to on-device ONNX inference, Ollama, model-backed extraction, and durable vector storage.

| Sample | What it demonstrates | Requirements |
| --- | --- | --- |
| [Getting started](GettingStarted/README.md) | Add, search, update, history, and delete | .NET 10 |
| [ONNX Local Inference](OnnxLocal/README.md) | 100% on-device private SLM memory extraction via ONNX Runtime & MEAI | .NET 10 |
| [Ollama](Ollama/README.md) | Local model-backed extraction and embeddings via [OllamaSharp](https://github.com/awaescher/OllamaSharp) | .NET 10, Ollama |
| [Microsoft Agent Framework memory](AgentFrameworkMemory/README.md) | Use Mem0Sharp as an `AIContextProvider` for a .NET agent | .NET 10, OpenAI API key |
| [Memory behaviors](MemoryBehaviors/README.md) | Normal, dreaming, random-thought, and personality-shaped memory | .NET 10, OpenAI API key |
| [PostgreSQL and OpenAI](PostgresOpenAI/README.md) | OpenAI models with PostgreSQL/pgvector persistence | .NET 10, Docker, API key |

Run a sample from the repository root:

```powershell
dotnet run --project .\samples\OnnxLocal\OnnxLocal.csproj
```
