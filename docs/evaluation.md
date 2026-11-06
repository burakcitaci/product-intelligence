# Evaluating retrieval and generation

The standard-library Python runner exercises the normal Product Hub API with isolated fixture products. Python 3.10+ is required only for evaluation.

## Offline checks

From the repository root:

```bash
python scripts/evaluate.py --dry-run
python -m unittest discover -s tests/evaluation -p test_evaluate.py -v
```

Dry-run validates the dataset and lists selected runs without authentication, network requests, model calls, or report writes.

There are 48 cases: 12 product types, German and English, baseline/challenge variants. Challenges cover conflicting facts, exact model identities, missing documents, unsupported certifications/performance, multilingual guidance, accessory confusion, insufficient attributes, unit conflicts, and malicious instructions. The original two-case retrieval baseline remains available.

The full matrix is 48 cases × four retrieval modes × facts on/off = 384 generations per model. The default cap is **four generations**, sequentially, within ten minutes, with a 180-second request timeout. Order is case → model → facts → mode. Defaults sample the first case's four modes with facts included; this is not a representative benchmark.

## Small live smoke run

Start the local API, SQL Server, Qdrant, and Ollama stack. Ingestion needs the embeddings model; generation needs the selected text model. Supply administrator credentials through environment variables.

PowerShell:

```powershell
$env:PRODUCTHUB_EVAL_EMAIL = 'admin@producthub.local'
$env:PRODUCTHUB_EVAL_PASSWORD = 'Admin123!' # Local demo account only
python scripts/evaluate.py --cases bottle-baseline-en --modes None --facts on --max-runs 1
```

Bash:

```bash
export PRODUCTHUB_EVAL_EMAIL=admin@producthub.local
export PRODUCTHUB_EVAL_PASSWORD='Admin123!' # Local demo account only
python scripts/evaluate.py --cases bottle-baseline-en --modes None --facts on --max-runs 1
```

The runner creates an evaluation catalog and uniquely prefixed fixture products and uploads fixture documents once per run. Subsequent language/mode comparisons reuse those fixtures. Existing products are not edited; drafts are never approved or published. Fixture data remains in the database for inspection; there is no destructive cleanup.

Generated SKUs contain the run ID to avoid collisions. The original SKU remains in the snapshot; this dataset does not check verbatim reproduction of that original SKU.

Reports default to the ignored `.artifacts/evaluation/` directory. Example bounded comparison:

```bash
python scripts/evaluate.py --cases kettle-challenge-en --modes Naive,Advanced,Graph --facts off --max-runs 3 --output .artifacts/evaluation/injection.json
```

Ctrl+C saves an interrupted report. Reports are atomically replaced after each attempt and on interruption. A timeout/interruption can leave generation running on the server: inspect the saved product ID and idempotency key before retrying. Unknown requests are never automatically retried.

## Deliberate full benchmark

```bash
python scripts/evaluate.py --dry-run --max-runs 384 --models qwen3:8b
python scripts/evaluate.py --max-runs 384 --max-seconds 86400 --models qwen3:8b --hardware "Machine and GPU description" --model-revision "Recorded Ollama model digest"
```

Full benchmarks are opt-in and may take hours. Limits are 1–512 runs, 1–86400 seconds overall, 1–600 seconds per request, and Top-K 1–20. In-flight HTTP calls also respect remaining overall time.

Filters: `--languages de`; `--facts on|off|both`; comma-separated exact `--cases` IDs, `--modes`, and `--models`. Increase the run cap or use separate reports to balance multiple models. The API still enforces its model allowlist.

## Metrics and limits

Reports include the input snapshot and SHA-256, model tags, manually supplied revision/hardware context, prompt version, generation settings, wall time, provider token usage, draft content, source citations, errors, and grouped comparisons. Credentials and raw authentication errors are excluded.

- **Draft contract validity:** returned field types and size/count bounds. Raw model JSON is not exposed by the API; the summary separately counts `invalid-json` API failures.
- **Factual coverage proxy:** fraction of case-specific required regex patterns found when facts are included. This is not a general semantic faithfulness score.
- **Prohibited claim matches:** case-specific regex matches. Negation/paraphrasing can cause false positives or negatives; human review is required.
- **Source recall at K:** expected source filenames found in saved audit sources. Null without retrieval or expected evidence. This is document-level recall, not chunk recall.
- **Latency/tokens:** wall time and provider token counts when available. First-attempt wall time includes provisioning; use generation duration for inference comparisons.
- **Expected no-context:** missing-document RAG cases pass only for HTTP 503 with `rag-no-context`.
- **Human editorial acceptance:** null until reviewed. Language quality, semantic claim support, and advisory-versus-specification wording are not automatically verified.

Passing checks do not establish that all claims are supported. Claim review is implemented, but its conservative classifications still require human editorial review.

The supplied model revision is recorded as unverified. Stable snapshots improve comparability, but mutable model tags and stochastic generation prevent a bit-for-bit reproducibility guarantee.

Exit code zero means the bounded selection completed and every attempt passed. Setup errors, transport failures, interruption/time budgets, missing evidence, and failed checks return nonzero.

## Verification

Sixteen offline tests cover matrix bounds, filters, fixture reuse, scoring, facts exclusion, missing sources, expected failures, interrupted checkpoints, credential exclusion, dry-run, and HTTP errors. A bounded live four-mode smoke and an optional-reranker experiment have now run; see [acceptance results](evaluation-results-2026-11-04.md).



Schema V2 permits zero to five highlights. Reports now include deterministic per-status claim counts, evidence warnings and whether human acknowledgement is required. These are the conservative claim checker's classifications, not independently verified truth labels. They are kept separate from human editorial acceptance. Group summaries aggregate these counts; all original metadata, parameters and source revision snapshots remain in each result.


## Risk gates and offline rescoring

Scoring version 2 makes Conflict or Unsupported classifications fail the attempt's review-risk gate, even if required regex facts appear elsewhere. Unverified claims are not promoted to verified facts; human acceptance remains unset. Old drafts without a review are marked claimReviewAvailable=false, rather than assigned invented support counts.

Rescore saved outputs without credentials, API requests or model calls:

```bash
python scripts/evaluate.py --rescore-report .artifacts/evaluation/example.json
```

This creates a separate report with input SHA-256 provenance and preserves the original. Windows checkpoint replacement retries transient sharing errors up to five times; only file I/O is retried, never a generation request.

scripts/acceptance_api.py runs bounded live contract checks with disposable fixtures. It intentionally approves its own generated fixture to exercise acknowledgement/publishing, so use an isolated acceptance database. This differs from evaluate.py, which never approves or publishes. scripts/evaluate_reranker.py runs one bounded local reranker experiment without changing production ranking. Both accept the same PRODUCTHUB_EVAL_EMAIL and PRODUCTHUB_EVAL_PASSWORD environment variables. Do not point them at unrelated databases.
