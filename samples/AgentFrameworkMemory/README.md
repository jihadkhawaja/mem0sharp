# Microsoft Agent Framework memory sample

This sample connects Mem0Sharp to a Microsoft Agent Framework `AIAgent` through the `AIContextProvider` extension point. Mem0Sharp recalls relevant user memories before each agent invocation and stores new user messages after each invocation.

The sample uses the in-memory store, so it needs no database. Replace `new MemoryService()` with a configured persistent store when moving the pattern into an application.

## Prerequisites

- .NET 10 SDK
- An OpenAI API key, or an OpenAI-compatible endpoint

Copy the example configuration and add your API key:

```powershell
Copy-Item .\samples\AgentFrameworkMemory\sampleconfig.example.yaml .\samples\AgentFrameworkMemory\sampleconfig.local.yaml
```

Edit `sampleconfig.local.yaml` to set `openAi.apiKey`. The `openAi.endpoint` value can point to an OpenAI-compatible server and must include the provider root, for example `https://api.openai.com/v1/`.

## Run it

From the repository root:

```powershell
dotnet run --project .\samples\AgentFrameworkMemory\AgentFrameworkMemory.csproj
```

The program runs two turns for the same user. The second turn demonstrates that the agent can use a preference stored during the first turn. Type `exit` to stop.

## How it works

`Mem0ContextProvider` implements Agent Framework's `AIContextProvider`:

- `ProvideAIContextAsync` searches Mem0Sharp before the model is invoked.
- The host stores the latest user message after the model responds.
- The memory filter scopes records to the sample user.

This is an integration sample, not an official Microsoft or Mem0-supported adapter.
