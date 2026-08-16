# Evaluation

The committed fixture is intentionally a small harness validation set: two
fictional conversations and 22 questions. It is useful for regression checks
and comparing configuration tradeoffs, but it is not evidence of production
benchmark performance. Generated reports identify the dataset and include
Wilson 95% question-level intervals; those intervals describe sampling
uncertainty and do not capture LLM/provider variance. Use the external
`memory-benchmarks` datasets for broader claims.

Mem0Sharp ships with a runnable evaluation harness in [evaluation/](../evaluation/README.md) that measures how well the library stores and retrieves memories across its options and memory behaviors. It uses PostgreSQL/pgvector as the database and follows the ingest → search → answer → judge pipeline used by the LOCOMO benchmark in the [Mem0 evaluation suite](https://github.com/mem0ai/memory-benchmarks), with a self-contained fictional dataset so no download is required.

## Method

```mermaid
flowchart LR
    A[48 conversation turns<br/>2 fictional conversations] --> B[Ingest with scenario options<br/>behavior, infer, dedup, conflict resolution]
    B --> C[(PostgreSQL/pgvector<br/>fresh tables per scenario)]
    D[22 questions<br/>4 categories] --> E[Search with scenario options<br/>hybrid, rerank, threshold]
    C --> E
    E --> F[Answer generation<br/>from retrieved memories]
    F --> G[LLM judge vs<br/>reference answer]
    G --> H[Accuracy, retrieval hit rate,<br/>latency, memories stored]
```

Each scenario ingests the same two multi-session conversations into fresh, scenario-scoped PostgreSQL tables, then evaluates 22 questions:

| Category | Questions | What it tests |
| --- | --- | --- |
| Single-hop | 8 | Recalling one stated fact |
| Multi-hop | 4 | Combining two or more facts |
| Temporal | 4 | Reasoning about how facts changed over time |
| Adversarial | 6 | Refusing to answer questions the conversations never cover |

**Accuracy (J-score)** is the share of answers judged CORRECT by an LLM judge against a reference answer, using the LOCOMO benchmark's unified judge rules: JSON verdicts with reasoning, partial credit for list answers, paraphrase and date tolerance (±14 days, durations within 50%), and semantic-overlap matching. Adversarial questions score CORRECT only when the system declines to guess. Alongside the judge score, the harness reports the answer-quality metrics used by popular memory evaluations: **token-level F1** and **BLEU-1** between the generated and reference answers. **Retrieval hit rate** is the share of answerable questions where at least one expected evidence string appears in the retrieved memories, which separates retrieval quality from answer-generation quality.

## Scenario matrix

| Scenario | What it varies |
| --- | --- |
| `baseline` | Default pipeline: LLM extraction, hybrid search, dedup on |
| `realistic-long-haul` | Long-horizon retrieval tuned for recency-aware personal memory and preference drift |
| `stale-forget` | Retention pruning to simulate forgetting stale or superseded facts |
| `no-hybrid` | Semantic vector search only |
| `llm-rerank` | Adds `LlmReranker` |
| `conflict-resolution` | Adds `LlmMemoryConflictResolver` (ADD/UPDATE/DELETE/NONE decisions) |
| `no-dedup` | Deduplication off |
| `infer-off` | Raw message storage, no LLM extraction |
| `strict-threshold` | Search threshold raised to 0.3 |
| `behavior-dreaming` | Dreaming memory behavior |
| `behavior-random-thoughts` | Random-thoughts memory behavior |
| `behavior-personal-memory` | First-person persona-shaped memory behavior |

## Results

### Latest live scenario matrix (2026-08-16 12:14 UTC)

Authoritative full run against the composed PostgreSQL/pgvector database with `gpt-5.6-luna` for extraction, answering, and judging, and `text-embedding-3-small` for embeddings via `Microsoft.Extensions.AI`. This run covers 12 scenarios over the built-in 22-question fixture, including the long-term memory lifecycle scenarios `realistic-long-haul` and `stale-forget`.

Raw detailed reports: [Markdown](../evaluation/results/evaluation-20260816-121410.md) and [JSON](../evaluation/results/evaluation-20260816-121410.json).
Interactive visualizer: [Mem0Sharp Graph Memory Visualizer](../evaluation/visualizer/index.html).

| Scenario | Accuracy (J) | Mean F1 | Mean BLEU-1 | Retrieval hit rate | Memories | Mean search (ms) | Ingest (s) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| baseline | 100% (22/22; 95% CI 85%-100%) | 0.50 | 0.27 | 100% (16/16; 95% CI 81%-100%) | 34 | 186 | 14.9 |
| realistic-long-haul | 91% (20/22; 95% CI 72%-97%) | 0.47 | 0.25 | 94% (15/16; 95% CI 72%-99%) | 31 | 185 | 16.1 |
| stale-forget | 91% (20/22; 95% CI 72%-97%) | 0.44 | 0.22 | 100% (16/16; 95% CI 81%-100%) | 33 | 188 | 15.0 |
| no-hybrid | 91% (20/22; 95% CI 72%-97%) | 0.47 | 0.24 | 94% (15/16; 95% CI 72%-99%) | 32 | 198 | 12.1 |
| llm-rerank | 95% (21/22; 95% CI 78%-99%) | 0.46 | 0.24 | 100% (16/16; 95% CI 81%-100%) | 36 | 2101 | 13.3 |
| conflict-resolution | 95% (21/22; 95% CI 78%-99%) | 0.47 | 0.25 | 100% (16/16; 95% CI 81%-100%) | 28 | 181 | 31.7 |
| no-dedup | 95% (21/22; 95% CI 78%-99%) | 0.46 | 0.23 | 100% (16/16; 95% CI 81%-100%) | 33 | 185 | 13.6 |
| infer-off | 100% (22/22; 95% CI 85%-100%) | 0.53 | 0.29 | 100% (16/16; 95% CI 81%-100%) | 57 | 178 | 4.7 |
| strict-threshold | 91% (20/22; 95% CI 72%-97%) | 0.44 | 0.21 | 94% (15/16; 95% CI 72%-99%) | 30 | 181 | 14.8 |
| behavior-dreaming | 100% (22/22; 95% CI 85%-100%) | 0.49 | 0.26 | 100% (16/16; 95% CI 81%-100%) | 37 | 181 | 15.0 |
| behavior-random-thoughts | 91% (20/22; 95% CI 72%-97%) | 0.45 | 0.23 | 94% (15/16; 95% CI 72%-99%) | 30 | 175 | 13.0 |
| behavior-personal-memory | 91% (20/22; 95% CI 72%-97%) | 0.45 | 0.22 | 100% (16/16; 95% CI 81%-100%) | 29 | 179 | 13.1 |

### Accuracy by category

| Scenario | Single-hop | Multi-hop | Temporal | Adversarial |
| --- | --- | --- | --- | --- |
| baseline | 100% | 100% | 100% | 100% |
| realistic-long-haul | 88% | 100% | 75% | 100% |
| stale-forget | 88% | 100% | 75% | 100% |
| no-hybrid | 100% | 100% | 50% | 100% |
| llm-rerank | 100% | 100% | 75% | 100% |
| conflict-resolution | 88% | 100% | 100% | 100% |
| no-dedup | 100% | 100% | 75% | 100% |
| infer-off | 100% | 100% | 100% | 100% |
| strict-threshold | 100% | 100% | 50% | 100% |
| behavior-dreaming | 100% | 100% | 100% | 100% |
| behavior-random-thoughts | 100% | 75% | 75% | 100% |
| behavior-personal-memory | 88% | 100% | 75% | 100% |

### Harness validation (Self-test mode)

The deterministic self-test run validates the harness plumbing and retrieval-only behavior against the composed PostgreSQL/pgvector database without needing an API key. It confirms the realistic long-term scenarios run correctly in the local path and surface the expected retrieval hit rates for the lifecycle-focused cases.

| Scenario | Mode | Accuracy | Retrieval hit rate | Memories | Mean search (ms) |
| --- | --- | --- | --- | --- | --- |
| baseline | retrieval-only, deterministic local embeddings | n/a | 81% (13/16) | 57 | 5 |
| realistic-long-haul | retrieval-only, deterministic local embeddings | n/a | 81% (13/16) | 57 | 3 |
| stale-forget | retrieval-only, deterministic local embeddings | n/a | 81% (13/16) | 57 | 2 |
| strict-threshold | retrieval-only, deterministic local embeddings | n/a | 12% (2/16) | 57 | 3 |

## Interpreting the measured results

- **Baseline Excellence**: The standard pipeline (`baseline`) achieved **100% accuracy (22/22)** with a **100% retrieval hit rate** across single-hop, multi-hop, temporal, and adversarial queries.
- **Raw Context Retrieval**: `infer-off` achieved 100% accuracy and the highest mean F1 (0.53) by storing all 57 raw conversation messages.
- **Behavior-Aware Memory**: `behavior-dreaming` achieved 100% accuracy with 37 extracted consolidated memories. `behavior-personal-memory` and `behavior-random-thoughts` both achieved 91% accuracy.
- **Conflict Resolution & Reranking**: `conflict-resolution` and `llm-rerank` reached 95% accuracy with 100% retrieval hit rate. `conflict-resolution` produced the most compact memory footprint (28 memories) while maintaining 100% temporal reasoning accuracy.
- **Adversarial Precision**: Adversarial accuracy was **100% across all 12 scenarios**, demonstrating that the system consistently rejects ungrounded questions.

## Reproducing and publishing results

To rerun the matrix:

```powershell
docker compose -f .\evaluation\compose.yaml up -d
Copy-Item .\evaluation\Mem0Sharp.Evaluation\evalconfig.example.yaml .\evaluation\Mem0Sharp.Evaluation\evalconfig.local.yaml
# add your API key to evalconfig.local.yaml, then:
dotnet run --project .\evaluation\Mem0Sharp.Evaluation\Mem0Sharp.Evaluation.csproj
```

To evaluate a broader or application-specific fixture, pass a JSON dataset
using the schema in
[`evaldataset.example.json`](../evaluation/Mem0Sharp.Evaluation/evaldataset.example.json):

```powershell
dotnet run --project .\evaluation\Mem0Sharp.Evaluation\Mem0Sharp.Evaluation.csproj -- --dataset .\path\to\dataset.json
```

The JSON contains a dataset `name`, `conversations` with dated sessions and
speaker turns, and `questions` with `conversationId`, `category`,
`expectedAnswer`, and `evidence`. The harness validates unique IDs and question
references before starting a database run.

The run writes Markdown and JSON reports next to the executable under `results/`.
