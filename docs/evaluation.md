# Evaluation

The committed fixture is intentionally a small harness validation set: two
fictional conversations and 24 questions. It is useful for regression checks
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
    D[24 questions<br/>4 categories] --> E[Search with scenario options<br/>hybrid, rerank, threshold]
    C --> E
    E --> F[Answer generation<br/>from retrieved memories]
    F --> G[LLM judge vs<br/>reference answer]
    G --> H[Accuracy, retrieval hit rate,<br/>latency, memories stored]
```

Each scenario ingests the same two multi-session conversations into fresh, scenario-scoped PostgreSQL tables, then evaluates 24 questions:

| Category | Questions | What it tests |
| --- | --- | --- |
| Single-hop | 8 | Recalling one stated fact |
| Multi-hop | 6 | Combining two or more facts |
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

### Latest live scenario matrix (2026-08-15)

Authoritative full run against the composed PostgreSQL/pgvector database with `gpt-5.6-luna` for extraction, answering, and judging, and `text-embedding-3-small` for embeddings. This run includes the new long-term memory lifecycle scenarios: `realistic-long-haul` and `stale-forget`.

Raw detailed reports: [Markdown](../evaluation/results/evaluation-20260815-065918.md) and [JSON](../evaluation/results/evaluation-20260815-065918.json).

| Scenario | Accuracy (J) | Mean F1 | Mean BLEU-1 | Retrieval hit rate | Memories | Mean search (ms) | Ingest (s) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| baseline | 86% (19/22; 95% CI 67%-95%) | 0.44 | 0.21 | 94% (15/16; 95% CI 72%-99%) | 34 | 346 | 28.2 |
| realistic-long-haul | 91% (20/22; 95% CI 72%-97%) | 0.45 | 0.22 | 100% (16/16; 95% CI 81%-100%) | 29 | 279 | 17.7 |
| stale-forget | 86% (19/22; 95% CI 67%-95%) | 0.44 | 0.21 | 94% (15/16; 95% CI 72%-99%) | 35 | 430 | 21.5 |
| no-hybrid | 95% (21/22; 95% CI 78%-99%) | 0.46 | 0.23 | 100% (16/16; 95% CI 81%-100%) | 32 | 457 | 17.9 |
| llm-rerank | 100% (22/22; 95% CI 85%-100%) | 0.50 | 0.26 | 100% (16/16; 95% CI 81%-100%) | 33 | 9572 | 20.3 |
| conflict-resolution | 95% (21/22; 95% CI 78%-99%) | 0.46 | 0.23 | 94% (15/16; 95% CI 72%-99%) | 27 | 270 | 30.2 |
| no-dedup | 95% (21/22; 95% CI 78%-99%) | 0.45 | 0.21 | 94% (15/16; 95% CI 72%-99%) | 33 | 433 | 18.2 |
| infer-off | 100% (22/22; 95% CI 85%-100%) | 0.52 | 0.27 | 100% (16/16; 95% CI 81%-100%) | 57 | 323 | 6.3 |
| strict-threshold | 91% (20/22; 95% CI 72%-97%) | 0.46 | 0.24 | 94% (15/16; 95% CI 72%-99%) | 28 | 393 | 20.2 |
| behavior-dreaming | 95% (21/22; 95% CI 78%-99%) | 0.46 | 0.23 | 81% (13/16; 95% CI 57%-93%) | 37 | 345 | 20.4 |
| behavior-random-thoughts | 50% (11/22; 95% CI 31%-69%) | 0.26 | 0.06 | 69% (11/16; 95% CI 44%-86%) | 16 | 273 | 16.9 |
| behavior-personal-memory | 100% (22/22; 95% CI 85%-100%) | 0.50 | 0.27 | 94% (15/16; 95% CI 72%-99%) | 21 | 391 | 20.2 |

### Harness validation (2026-08-15)

The deterministic self-test run validates the harness plumbing and retrieval-only behavior against the composed PostgreSQL/pgvector database without needing an API key. It confirms the realistic long-term scenarios run correctly in the local path and surface the expected retrieval hit rates for the lifecycle-focused cases.

| Scenario | Mode | Accuracy | Retrieval hit rate | Memories | Mean search (ms) |
| --- | --- | --- | --- | --- | --- |
| baseline | retrieval-only, deterministic local embeddings | n/a | 94% (15/16) | 57 | 6 |
| realistic-long-haul | retrieval-only, deterministic local embeddings | n/a | 94% (15/16) | 57 | 24 |
| stale-forget | retrieval-only, deterministic local embeddings | n/a | 94% (15/16) | 57 | 6 |
| strict-threshold | retrieval-only, deterministic local embeddings | n/a | 69% (11/16) | 57 | 3 |

## Interpreting the measured results

- The new life-cycle features are working in the live benchmark path: `realistic-long-haul` reached 91% accuracy with a 100% retrieval hit rate, while `stale-forget` remained stable at 86% accuracy and 94% retrieval hit rate.
- `llm-rerank` produced the strongest answer quality in this run, reaching 100% accuracy, but it paid for that with a much slower search path at roughly 9.6 seconds per scenario.
- Behavior-aware memory remains a differentiator: `behavior-personal-memory` reached 100% accuracy and `behavior-dreaming` reached 95%, while `behavior-random-thoughts` was the weakest scenario at 50% accuracy and 69% retrieval hit rate.
- Temporal reasoning remains the hardest category, which is expected for a small evaluation fixture; the results are best interpreted as directional rather than definitive for production workloads.

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

## Interpreting the measured results

- **Adversarial accuracy is 100% in every scenario**. With retrieval scoped correctly, Mem0Sharp declines to answer questions the conversations never covered instead of hallucinating.
- **`infer-off`, `behavior-dreaming`, and `behavior-personal-memory` share the top score at 96%**. `infer-off` is the fastest ingest path at 6.9 seconds and preserves full conversation context; dreaming stores the richest associative set at 126 memories; personal memory reaches the highest F1 at 0.52 among the behavior scenarios.
- **The behavior-aware search correction matters**. The first run produced 0% retrieval for all non-normal behaviors because the evaluator used the factual-only default. The authoritative run explicitly selects each scenario behavior, restoring retrieval to 100% for dreaming and personal memory and 89% for random thoughts.
- **`random-thoughts` remains the weakest factual-recall behavior** at 58% accuracy and 0.23 F1. Its 89% retrieval hit rate shows that the main loss is answer quality and associative dilution, not complete retrieval failure; use it for ideation rather than factual recall.
- **`no-hybrid` performed well in this run** at 92% accuracy and 100% retrieval hit rate, but this small fixture is not enough to conclude that keyword fusion is unnecessary. Hybrid search remains the safer default for exact names, places, and phrases.
- **`llm-rerank` did not pay off here**: it took 24.2 seconds per question set and reached 75% accuracy, lower than baseline. Keep reranking for larger candidate pools where vector order is demonstrably noisy.
- **`strict-threshold` (0.3) reduced accuracy to 75% and retrieval to 94%**, while the default threshold retained 94% retrieval. Treat 0.3 as a workload-specific setting rather than a general default.
- **Temporal reasoning remains difficult**, ranging from 50% to 75% for most scenarios. The small sample and wide confidence intervals mean these category differences should be treated as directional rather than definitive.
