# PostgreSQL and OpenAI sample

This sample uses an OpenAI-compatible client for extraction and embeddings, then stores memories and vectors in PostgreSQL with pgvector.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker, or an existing PostgreSQL server with pgvector
- An OpenAI API key

The configured `text-embedding-3-small` model returns 1536 dimensions. The sample's `embeddingDimensions` value must change if you select a model with a different vector size.

## Configure the sample

Copy `sampleconfig.example.yaml` to `sampleconfig.local.yaml`, then add your API key to the local file:

```powershell
Copy-Item .\samples\PostgresOpenAI\sampleconfig.example.yaml .\samples\PostgresOpenAI\sampleconfig.local.yaml
```

`sampleconfig.local.yaml` is ignored by Git. It contains the OpenAI endpoint, API key, model names, PostgreSQL connection string, embedding dimensions, and table name used by the application.

## Start PostgreSQL

From the repository root:

```powershell
docker compose -f .\samples\PostgresOpenAI\compose.yaml up -d
```

The template's default connection string is:

```text
Host=localhost;Port=5432;Database=mem0;Username=postgres;Password=postgres
```

Edit `postgres.connectionString` in `sampleconfig.local.yaml` to override it.

## Run it

```powershell
dotnet run --project .\samples\PostgresOpenAI\PostgresOpenAI.csproj
```

The application initializes the vector extension and tables, extracts memories from a conversation, persists their embeddings, and performs a scoped similarity search.

For production, use secret management rather than shell history, provide a long-lived `HttpClient`, use restricted database credentials, and set `CreateExtension = false` after an administrator has installed pgvector.

Stop the sample database with:

```powershell
docker compose -f .\samples\PostgresOpenAI\compose.yaml down
```
