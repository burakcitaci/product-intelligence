# Durable generation jobs

The product UI queues generation on the API. SQL Server owns job identity and state; reopening a product reconnects to its latest 50 jobs. The existing synchronous generation endpoint remains available for API clients and the evaluation runner.

## API

All routes require an authenticated administrator. These routes share the application's existing administrator-wide access model.

| Method | Route | Result |
| --- | --- | --- |
| POST | /api/products/{productId}/generation-jobs | HTTP 202, persisted job and Location header |
| GET | /api/products/{productId}/generation-jobs | Latest 50 jobs, newest first |
| GET | /api/generation-jobs/{id} | Status, stage, cancellation flag, draft ID, safe error |
| POST | /api/generation-jobs/{id}/cancel | Updated job; terminal jobs remain terminal |

POST uses the same JSON settings as synchronous generation and requires Idempotency-Key. Validation checks language, model allowlist, retrieval mode, Top-K and product existence before queuing. The selected model is resolved when queued; product evidence, configured prompt and sampling settings are snapshotted by the generation service at execution time. Changing product facts while queued therefore affects the eventual draft and its saved evidence, rather than silently preserving obsolete facts.

The unique (ProductId, IdempotencyKey) index makes repeated submissions return the same job. It also shares that key with the generation audit, so completed synchronous work can be reused. Reusing a key with different options returns the original operation. Use a fresh key for a deliberate retry or changed settings. The UI stores the pending product/key/payload in session storage before enqueue. Retrying unchanged settings after a lost response reuses the same key, and polling can reconcile a job by its returned idempotencyKey. Confirmed responses clear the pending submission. Changing settings deliberately creates a distinct request. Browser storage restrictions limit persistence to the current in-memory view.

## Worker and progress

GenerationJobWorker is an API-hosted background service, enabled by default outside the Testing environment. Set Ai:DisableJobWorker=true to run an API without a worker. Migrate before starting any worker. A single worker runs one model request at a time per API host. Multiple API instances coordinate each job through conditional SQL updates and a lease token; total model concurrency is the number of worker-enabled hosts. The synchronous endpoint is separately request-driven and does not share this queue's concurrency bound.

States are Queued, Running, Succeeded, Failed and Cancelled. Running stages are Retrieving, Generating and Validating; successful work ends at Ready. The worker forwards progress from the existing generation service. It records the completed draft ID and never approves or publishes content.

The UI polls every 1.5 seconds while a product is open, retries connection failures and reloads drafts when an observed job completes. Polling stops when leaving the product. Server work continues across navigation and browser refresh; the catalog/product/language URL restores the product after refresh, including products beyond the first list page. Selecting the product again also reconnects. Existing draft content remains visible during generation. Terminal job errors appear in recent generations.

## Cancellation, leases and restart recovery

Claiming a job uses an atomic Queued-to-Running conditional update and a new lease token. A separate SQL context renews a two-minute lease every second and checks cancellation. Every progress/terminal update is fenced by the lease token. Cancellation of a queued job prevents its execution. Running cancellation reaches retrieval/model calls through a cancellation token; a provider that ignores cancellation may take longer to stop. A draft committed immediately before cancellation remains a successful result. Cancellation never revokes an already saved draft.

Expired Running jobs are inspected after restart and before taking more work:

- A committed draft with the matching product/key restores Succeeded without another model call.
- An operation with no generation audit can return to Queued, unless cancellation was requested.
- An existing generation audit without a saved draft produces Failed/interrupted (or Cancelled). The worker does not repeat an ambiguous model call automatically.

Only pre-inference work with no generation identity is automatically requeued. Provider transport retries remain bounded by the Ollama adapter (two attempts); invalid JSON/output is not repeatedly repaired. An API crash cannot guarantee cancellation of an external provider, so interrupted requests need inspection and a fresh key for a deliberate retry.

## Persistence and verification

Migration 20261104120000_AddGenerationJobs creates GenerationJobs with command JSON, requester identity, state/stage, lease, cancellation, result and error fields. Product deletion cascades jobs. There is currently no automatic job retention cleanup, distributed global concurrency quota, SSE stream or per-user private queue.

Tests cover key replay/reconnect, queued cancellation, worker completion, recovery of committed results, and refusal to replay uncertain calls. Further contract tests cover SQL lexical retrieval and audit snapshots. Linux CI runs the SQL Server Testcontainer suite. The complete 32-test integration suite and 17 live API checks passed on 2026-11-04. Browser tests cover refresh recovery and reuse of a lost enqueue key. Windows application control still blocked the final rebuilt unit-test binary. See [acceptance results](../evaluation-results-2026-11-04.md) for the exact scope.


## Auditing unsuccessful attempts

GET /api/products/{productId}/generations is an administrator-only, paginated audit of all product attempts. It includes synchronous generations and durable jobs, including jobs cancelled or failed before any AiGeneration/draft existed. Matched job/generation pairs are joined by product and idempotency key and appear once. This reads existing records; no migration or backfill is needed.

Query parameters are page (positive), pageSize (1–100; default 25), status (all, failed, succeeded, cancelled, inprogress, queued), and language (all, de, en). Filters, total counts and pagination run on the server. Results expose job/generation/draft IDs, language, model and retrieval/facts choices, state/stage, timestamps, duration, recorded error code/message and optional full generation metadata. A cancelled synchronous request is classified as Cancelled while its original generation metadata remains unchanged.

A saved job supplies its original safe error message. Historical synchronous failures that only retained an error code receive a standard explanation for that code; the original exception text cannot be recovered. Missing inference parameters, prompt data, tokens or evidence are displayed as unavailable rather than filled from today's configuration. Orphan-job duration includes queue time; generation duration comes from the recorded generation audit.

Model audit is now available even when a product/language has no draft. It defaults to all languages and statuses, provides status/language filters and paging, refreshes when observed jobs change, and allows manual refresh for requests from other clients. Active attempts are polled while visible. Failed rows show the error directly, with full IDs, trace, settings and evidence under Attempt details. Only attempts with a saved draft provide a draft-view action. Recent generations keeps its compact status summary and links to Model audit for failed/cancelled jobs.

Audit enhancement verification: solution/frontend builds succeed and all 20 desktop/mobile browser checks pass, confirming failed attempts are visible without a saved draft. The dedicated SQL integration test covers synchronous/job failures, orphan cancellation, successful draft linking, deduplication, filters, paging and isolation, but local execution and a local API launch were blocked loading the rebuilt ProductHub.Application assembly by Windows application control (0x800711C7). This new endpoint's runtime check is therefore not claimed passing locally.
