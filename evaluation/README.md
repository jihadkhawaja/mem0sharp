# Mem0Sharp evaluation harness

This console application measures how well Mem0Sharp stores and retrieves memories across its options and memory behaviors, using PostgreSQL/pgvector as the database. It follows the ingest → search → answer → judge pipeline used by the LOCOMO benchmark in the [Mem0 evaluation suite](https://github.com/mem0ai/memory-benchmarks), but ships with a self-contained fictional dataset so no download is required.

The built-in fixture is a small regression harness, not a production benchmark.
Use `--dataset` with the schema in
[`Mem0Sharp.Evaluation/evaldataset.example.json`](Mem0Sharp.Evaluation/evaldataset.example.json)
to run a broader or application-specific dataset. Reports include the selected
dataset size and Wilson 95% question-level intervals; intervals do not capture
LLM/provider variance.

## What it measures

The harness runs a matrix of scenarios. Each scenario ingests two multi-session conversations (48 turns total) into fresh PostgreSQL tables, then answers 24 questions per scenario:

| Category | Questions | What it tests |
| --- | --- | --- |
| Single-hop | 8 | Recalling one stated fact |
| Multi-hop | 6 | Combining two or more facts |
| Temporal | 4 | Reasoning about how facts changed over time |
| Adversarial | 6 | Refusing to answer questions the conversations never cover |

Metrics per scenario:

- **Accuracy (J-score)** — share of answers judged CORRECT by an LLM judge against the reference answer, using the LOCOMO benchmark's unified judge rules (JSON verdicts with reasoning, partial credit, paraphrase and date tolerance). Adversarial questions score CORRECT only when the system declines to guess.
- **Mean F1 / BLEU-1** — token-overlap answer-quality metrics between generated and reference answers, standard in memory evaluations.
- **Retrieval hit rate** — share of answerable questions where at least one expected evidence string appears in the retrieved memories.
- **Memories stored**, **mean search latency**, and **ingest time**.
- **Wilson 95% intervals** for accuracy and retrieval hit rate, with the underlying sample counts.

## Scenarios

`--list` prints the current matrix. The default set:

| Scenario | What it varies |
| --- | --- |
| `baseline` | Default pipeline: LLM extraction, hybrid search, dedup on |
| `no-hybrid` | Semantic vector search only |
| `llm-rerank` | Adds `LlmReranker` |
| `conflict-resolution` | Adds `LlmMemoryConflictResolver` (ADD/UPDATE/DELETE/NONE decisions) |
| `no-dedup` | Deduplication off |
| `infer-off` | Raw message storage, no LLM extraction |
| `strict-threshold` | Search threshold raised to 0.3 |
| `behavior-dreaming` | Dreaming memory behavior |
| `behavior-random-thoughts` | Random-thoughts memory behavior |
| `behavior-personal-memory` | First-person persona-shaped memory behavior |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker (for the composed PostgreSQL/pgvector database)
- An OpenAI API key for the full evaluation (not needed for `--self-test`)

## Start the database

From the repository root:

```powershell
docker compose -f .\evaluation\compose.yaml up -d
```

PostgreSQL listens on port **5433** (database `mem0eval`) so it does not collide with the sample database on 5432. Stop it with:

```powershell
docker compose -f .\evaluation\compose.yaml down
```

Add `-v` to also delete the stored evaluation data.

## Configure

```powershell
Copy-Item .\evaluation\Mem0Sharp.Evaluation\evalconfig.example.yaml .\evaluation\Mem0Sharp.Evaluation\evalconfig.local.yaml
```

Add your API key to `evalconfig.local.yaml` (ignored by Git). The judge model defaults to the chat model; set `judgeModel` to use a different, ideally stronger, model for judging. The configured `embeddingDimensions` must match the embedding model.

## Run

```powershell
# Full matrix (all scenarios, LLM extraction + answering + judging)
dotnet run --project .\evaluation\Mem0Sharp.Evaluation\Mem0Sharp.Evaluation.csproj

# A subset of scenarios
dotnet run --project .\evaluation\Mem0Sharp.Evaluation\Mem0Sharp.Evaluation.csproj -- --scenario baseline,llm-rerank

# List scenarios
dotnet run --project .\evaluation\Mem0Sharp.Evaluation\Mem0Sharp.Evaluation.csproj -- --list

# Validate an external dataset without Docker or PostgreSQL
dotnet run --project .\evaluation\Mem0Sharp.Evaluation\Mem0Sharp.Evaluation.csproj -- --dataset .\evaluation\Mem0Sharp.Evaluation\evaldataset.example.json --validate-dataset

# Run the matrix against an external dataset
dotnet run --project .\evaluation\Mem0Sharp.Evaluation\Mem0Sharp.Evaluation.csproj -- --dataset .\path\to\dataset.json

# Plumbing check without an API key: deterministic local embeddings, retrieval metrics only
dotnet run --project .\evaluation\Mem0Sharp.Evaluation\Mem0Sharp.Evaluation.csproj -- --self-test
```

Reports are written as JSON and Markdown under `evaluation/Mem0Sharp.Evaluation/bin/<configuration>/net10.0/results/` (the `results` folder is created next to the executable). Build-output reports stay gitignored; to publish a run, copy its `.md` and `.json` into the committed [results/](results/README.md) folder and update the raw-report link and summary tables in [docs/evaluation.md](../../docs/evaluation.md).

Each scenario uses its own PostgreSQL tables (`eval_<scenario>`), reset at the start of every run, and scenario-scoped user ids, so reruns are reproducible and scenarios never contaminate each other.

## Cost and determinism notes

- A full run makes roughly 600–900 chat calls depending on the scenario mix (extraction per session, rerank and conflict-resolution calls, one answer and one judge call per question). Run a subset with `--scenario` while iterating.
- LLM extraction and judging are not perfectly deterministic; treat single-run numbers as approximate and rerun before drawing conclusions from small differences.
- The dataset is fictional and fixed; model updates and provider changes can shift results over time.
