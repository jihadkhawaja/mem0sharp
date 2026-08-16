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

### Latest live scenario matrix (2026-08-15 10:01 UTC)

Authoritative full run against the composed PostgreSQL/pgvector database with `gpt-5.6-luna` for extraction, answering, and judging, and `text-embedding-3-small` for embeddings. This run covers 12 scenarios over the built-in 22-question fixture, including the long-term memory lifecycle scenarios `realistic-long-haul` and `stale-forget`.

Raw detailed reports: [Markdown](../evaluation/results/evaluation-20260815-100116.md) and [JSON](../evaluation/results/evaluation-20260815-100116.json).
Interactive visualizer: [Mem0Sharp Graph Memory Visualizer](../evaluation/visualizer/index.html).

| Scenario | Accuracy (J) | Mean F1 | Mean BLEU-1 | Retrieval hit rate | Memories | Mean search (ms) | Ingest (s) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| baseline | 91% (20/22; 95% CI 72%-97%) | 0.45 | 0.22 | 100% (16/16; 95% CI 81%-100%) | 28 | 324 | 24.0 |
| realistic-long-haul | 91% (20/22; 95% CI 72%-97%) | 0.44 | 0.21 | 94% (15/16; 95% CI 72%-99%) | 30 | 298 | 18.2 |
| stale-forget | 91% (20/22; 95% CI 72%-97%) | 0.45 | 0.22 | 94% (15/16; 95% CI 72%-99%) | 32 | 305 | 18.5 |
| no-hybrid | 82% (18/22; 95% CI 61%-93%) | 0.43 | 0.20 | 94% (15/16; 95% CI 72%-99%) | 30 | 308 | 20.2 |
| llm-rerank | 86% (19/22; 95% CI 67%-95%) | 0.44 | 0.21 | 100% (16/16; 95% CI 81%-100%) | 27 | 2088 | 18.0 |
| conflict-resolution | 82% (18/22; 95% CI 61%-93%) | 0.41 | 0.17 | 81% (13/16; 95% CI 57%-93%) | 26 | 324 | 33.1 |
| no-dedup | 100% (22/22; 95% CI 85%-100%) | 0.47 | 0.24 | 100% (16/16; 95% CI 81%-100%) | 33 | 293 | 18.8 |
| infer-off | 100% (22/22; 95% CI 85%-100%) | 0.52 | 0.28 | 100% (16/16; 95% CI 81%-100%) | 57 | 367 | 7.1 |
| strict-threshold | 45% (10/22; 95% CI 27%-65%) | 0.29 | 0.09 | 31% (5/16; 95% CI 14%-56%) | 27 | 333 | 16.3 |
| behavior-dreaming | 86% (19/22; 95% CI 67%-95%) | 0.40 | 0.18 | 81% (13/16; 95% CI 57%-93%) | 34 | 314 | 19.9 |
| behavior-random-thoughts | 73% (16/22; 95% CI 52%-87%) | 0.30 | 0.10 | 56% (9/16; 95% CI 33%-77%) | 18 | 348 | 16.8 |
| behavior-personal-memory | 86% (19/22; 95% CI 67%-95%) | 0.42 | 0.19 | 81% (13/16; 95% CI 57%-93%) | 16 | 288 | 17.4 |

### Harness validation (2026-08-15)

The deterministic self-test run validates the harness plumbing and retrieval-only behavior against the composed PostgreSQL/pgvector database without needing an API key. It confirms the realistic long-term scenarios run correctly in the local path and surface the expected retrieval hit rates for the lifecycle-focused cases.

| Scenario | Mode | Accuracy | Retrieval hit rate | Memories | Mean search (ms) |
| --- | --- | --- | --- | --- | --- |
| baseline | retrieval-only, deterministic local embeddings | n/a | 81% (13/16) | 57 | 5 |
| realistic-long-haul | retrieval-only, deterministic local embeddings | n/a | 81% (13/16) | 57 | 3 |
| stale-forget | retrieval-only, deterministic local embeddings | n/a | 81% (13/16) | 57 | 2 |
| strict-threshold | retrieval-only, deterministic local embeddings | n/a | 12% (2/16) | 57 | 3 |

## Interpreting the measured results

- The long-term lifecycle scenarios both reached 91% accuracy; `realistic-long-haul` retained 94% retrieval hit rate, while `stale-forget` matched that retrieval rate after retention pruning.
- `no-dedup` and `infer-off` led the answer-quality matrix at 100% accuracy. `infer-off` also had the highest mean F1 at 0.52, while storing the full 57-memory conversation context.
- `llm-rerank` reached 86% accuracy with 100% retrieval, but its mean search latency rose to about 2.1 seconds. The result does not justify the added cost for this small fixture.
- Behavior-aware memory remains useful but workload-dependent: `behavior-dreaming` and `behavior-personal-memory` reached 86%, while `behavior-random-thoughts` reached 73% with 56% retrieval hit rate.
- The `strict-threshold` scenario reduced accuracy to 45% and retrieval to 31%, showing that a higher threshold needs workload-specific tuning.
- Adversarial accuracy was 100% in every scenario. Temporal reasoning remains the hardest category, so these small-fixture results are directional rather than production benchmark evidence.

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

The run writes Markdown and JSON reports next to the executable under `results/` (gitignored). To publish: copy both files into the committed [evaluation/results/](../evaluation/results/README.md) folder, then update the raw-report link above and the scenario summary and category tables, keeping the run date, model names, and mode from the report header. LLM extraction and judging are not perfectly deterministic; rerun before drawing conclusions from small differences.
