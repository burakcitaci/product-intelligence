# Implementation and acceptance results — 2026-11-04

All eight recommendation areas have implementations. This report separates shipped behavior, measured checks and remaining quality limits. The optional neural reranker was evaluated and is not enabled by default.

## Coverage

| Area | Implemented behavior |
| --- | --- |
| Evaluation | 48 German/English cases, bounded mode/facts/model matrix, checkpoints, source recall, claim-risk gates, offline rescoring |
| Structured generation | Explicit JSON schema, strict parsing, shared validation, bounded transport retries, persisted sampling parameters |
| Evidence and abstention | Saved claim/source snapshots, conservative classifications, sparse output, original-evidence editing, authenticated acknowledgement |
| Version review | Read-only saved-version diffs, settings/version labels, numeric edits, unsaved-change guards |
| Durable jobs | SQL idempotency, progress, cancellation, fenced leases and interrupted-job recovery; reload/deep-link reconnect; lost-response key reuse |
| Review operations | Server-backed catalog counts/filters/paging, session preferences and browser-local generation presets |
| Hybrid retrieval | Independent SQL and dense candidates, BM25/RRF, deduplication and context budgets |
| Graph/evidence lifecycle | Bilingual typed concepts, source/extraction revisions, bounded fresh-corpus cache, explicit supersession and failed-ingestion exclusion |

The obsolete root-page fallback was removed. Loading a product link now uses the shared workspace shell instead of briefly rendering the legacy interface.

## Automated and live checks

| Check | Result |
| --- | --- |
| .NET solution build | Success, zero warnings/errors |
| Frontend production build | Success; existing bundle-size advisory remains |
| SQL Server integration suite | 32 passed, including model/migrations, API review, queue and job recovery checks |
| Desktop/mobile browser suite | 18 passed against the production build in headless Edge with controlled API fixtures |
| Offline evaluator tests | 16 passed |
| Token-diff tests | 6 passed |
| Live API contracts | 17 passed against an updated local API and separate acceptance database |
| Live vector-store failure injection | HTTP 503; document persisted Failed with prepared chunks, rather than Ready |
| Unit suite | A 98-test run passed before the last two ingestion lifecycle tests were added. The final rebuilt test assembly was blocked by Windows application control, 0x800711C7; the 100-test revision is not claimed passing locally. |

Browser checks cover numeric comparison and keyboard focus, rejected navigation/discard, explicit saves, queue state, reload/reconnect, personal presets, supersession controls, lost-response key reuse, mobile overflow and delayed deep-link loading. SQL integration and live API tests verify actual backend behavior separately from browser fixtures.

The live contract run exercised real embeddings/Qdrant, graph cache hits and revision changes, exclusion of superseded sources in all three RAG modes, retained historical evidence, cycle rejection, idempotent jobs, cancellation, one real facts-off model job, original-context editing, approval acknowledgement and queue staleness. Approval affected only its disposable acceptance fixture. The user's existing ProductHub database and API on port 5099 were not changed. Temporary APIs on port 5199 were stopped; the acceptance database is retained for inspection.

## Real-model generation smoke

Scope: one English bottle case, facts included, all four retrieval modes, qwen3:8b, prompt product-description-v4, schema product-content-v2, temperature 0, seed 42 and 2,048 output tokens. Model digest at execution was not recorded. These are single observations, not a representative quality or latency benchmark.

| Mode | Wall time | Retrieval time | Input/output tokens | Claim-risk gate |
| --- | ---: | ---: | ---: | --- |
| None | 43.458 s | Not requested | 661 / 808 | Failed: numeric conflict |
| Naive | 18.948 s | 2.421 s | 880 / 348 | Passed |
| Advanced | 30.012 s | 2.573 s | 880 / 610 | Passed |
| Graph | 29.216 s | 2.517 s | 880 / 610 | Passed |

Every response satisfied the output contract. All three RAG runs retrieved the expected manual. The facts-only draft contained “75,0 ml” despite the authoritative 750 ml value. The deterministic checker correctly flagged that conflict. Scoring version 2 now fails Conflict/Unsupported claims even when the expected fact also appears elsewhere; earlier regex coverage alone was too permissive. The retained original report was rescored offline without repeating model calls. There were 27 Unverified claim classifications across these samples; passing a risk gate does not establish semantic truth or human editorial acceptance.

An earlier cold attempt reached its client timeout, and a Windows file-sharing lock interrupted a checkpoint. Both remain recorded separately. Checkpoint file replacement now has bounded retries that never repeat a model/API request. The warmed smoke is not presented as proof that cold starts meet the default timeout.

The compact tracked observations are in [generation smoke data](../tests/evaluation/results/2026-11-04-smoke.json). Full local reports remain under .artifacts/evaluation.

## Optional reranker decision

A separate experiment used three same-product passages and the exact query EX-900 cleaning after use. The implemented hybrid baseline already ranked target.md first (reciprocal rank 1). An optional local qwen3:8b reranking request used strict permutation output, at most three 1,200-character candidates, temperature 0, seed 42, 128 predicted tokens and a ten-second request timeout. It timed out after approximately 12.080 seconds of measured wall time including transport overhead. No neural ranking was returned, so no neural relevance improvement is claimed. The fallback kept the baseline ranking.

The decision is to retain BM25/RRF as the default and add no neural runtime dependency. One timed-out synthetic experiment is insufficient to choose a production reranker. The reusable experiment is scripts/evaluate_reranker.py; [its observations](../tests/evaluation/results/2026-11-04-reranker-smoke.json) distinguish fallback quality from a nonexistent neural result.

## Limits and rollout

The full 384-combination model matrix, human editorial acceptance and performance on other hardware remain unmeasured. The graph stays capped at 500 chunks and two hops; larger-corpus quality is not assumed from bounded-window tests. Neural reranking, larger graph windows and default-model changes remain evidence-gated decisions, not automatically enabled features.

Apply pending migrations through 20261104130000_AddDocumentRevisions before running this version against an existing database. Fresh migration and API checks succeeded in the isolated acceptance database and SQL Testcontainer. Existing documents retain legacy extraction-version metadata; new ingestion records its extraction/chunk policy. Superseded source IDs remain historical even if their replacement is later removed.

No commit, push or production deployment was performed. Account usage monitoring remains disabled as requested.


## Follow-up: failed-attempt audit

The subsequent audit enhancement includes failed synchronous generations and failed/cancelled jobs without drafts, combines matched job/generation records, and exposes status/language filters and paging. The audit is visible before a first draft exists and Recent generations links to it. Solution/frontend builds and 20 browser checks pass. Its new SQL integration test and local API launch were blocked by Windows application control loading the rebuilt ProductHub.Application assembly (0x800711C7); the earlier 32-test integration result above is the recorded baseline, not proof that this new endpoint executed locally. No additional migration is required.
