# UI and AI implementation roadmap

Status: implementation is in place for all eight stages, with build/focused verification and the runtime/live-quality limitations below. Draft comparison and the server-backed review queue are now included. Completion of code is separate from deployment and live-model acceptance.
Requested: 2026-11-04.

This roadmap records all improvements approved by the user after the UI/AI review. It is a work plan, not a claim that the features below are already implemented.

## Execution and usage policy

- Work in small, independently testable stages, in the order below. Update this file and the relevant README/architecture documentation after each completed stage.
- The user explicitly disabled account usage checks and warnings. Continue authorized implementation without polling usage.
- Do not purchase credits, redeem reset credits, or change the user's model/reasoning settings automatically. Respect the user's selected settings.
- Preserve the existing uncommitted Graph RAG work. Do not auto-commit, push, or deploy this roadmap's changes.
- Keep model-backed evaluation runs explicit and bounded; do not run the full model/mode matrix repeatedly during routine development.
- The user cancelled the daily follow-up. No scheduled implementation or monitoring is active. On 2026-11-04 the user explicitly requested starting immediately despite reported usage; the first bounded stage was implemented with a usage warning.

## Existing baseline

The project already has a compact review workspace, facts inclusion controls, None/Naive/Advanced/Graph retrieval modes, and generation/source auditing. The original Graph RAG baseline used a bounded request-local lexical chunk/concept graph. The most recent recorded validation was 74 unit tests, 13 generation API integration tests, and a successful frontend build; those checks do not establish live model output quality.

## Stage 1 — Real-model evaluation runner

The optional Python evaluator and its offline tests were removed so cloning and running the application does not require Python. Historical measurements and limitations remain in [acceptance results](evaluation-results-2026-11-04.md). Reintroduce a benchmark only when it can be maintained without increasing the default setup burden.

## Stage 2 — Schema-constrained generation

Status: implemented. Explicit schema, shared validation, strict parsing, bounded retries, generation parameters, and persisted audit are covered by adapter/API tests. Live schema behavior was exercised in all four modes and a facts-off worker job; see the acceptance report.

- Supply the exact generated-content JSON schema through the local Ollama adapter, retaining server-side validation.
- Explicitly configure and audit generation parameters that affect reproducibility.
- Handle schema/validation failures with bounded, observable recovery; preserve cancellation, idempotency, and failure auditing.
- Maintain compatibility for existing saved drafts and documented API clients.

Acceptance: adapter/contract tests verify the schema and error paths; benchmark results distinguish malformed output from unsupported content.

## Stage 3 — Claim validation, abstention, and evidence review

Status: implemented. Saved evidence/claim review, original-snapshot edits, zero-to-five-highlight sparse output, explicit insufficient-evidence warnings, authenticated acknowledgement gates and review UI are in place. Live evidence/acknowledgement contracts and SQL integration checks pass; the final unit-test binary remains restricted by Windows application control.

- Add stable claim/evidence identifiers and saved evidence snapshots suitable for review.
- Check numeric values, units, certifications, compatibility, accessories, and other high-impact specifications against authoritative facts.
- Detect missing or conflicting support and distinguish verified facts from advisory document guidance.
- Allow insufficient evidence to produce shorter content or an explicit review warning; do not force invented highlights to satisfy formatting.
- Link sentences/highlights to supporting facts or document excerpts in the review UI.
- Clearly mark unsupported, conflicting, advisory, and unverified content; clicking a claim opens its exact supporting passage.
- Keep citations and validation metadata separate from published marketing copy.

Acceptance: tests cover supported, unsupported, conflicting, and insufficient-evidence cases, including facts-off modes. Reviewers can inspect evidence without relying on a bare similarity score.

## Stage 4 — Draft comparison

Status: implemented. Read-only same-language version comparison, numeric/text/highlight diffs, audit/settings labels and unsaved-edit navigation guards are in place. Six diff tests and desktop/mobile browser interaction checks pass. The legacy loading fallback has been removed.

- Compare the selected draft with the current published version or another saved version.
- Highlight changes to titles, descriptions, highlights, and factual values; identify language/version/settings.
- Keep the published content visible while a replacement is being generated.
- Make unsaved edits and navigation loss clear.

Acceptance: meaningful difference cases and keyboard behavior are verified; comparing drafts never mutates or publishes them.

## Stage 5 — Durable generation jobs and progress

Status: implemented. SQL queue, idempotency, progress, per-host bounded worker, cancellation, fenced leases, safe restart recovery and UI reconnect are documented in architecture/generation-jobs.md. The SQL integration suite and live job contracts pass; browser reload/lost-response key recovery is covered.

- Move long-running retrieval/generation into durable jobs with explicit queued/running/stage/success/failure/cancelled states.
- Preserve existing generation idempotency and define backward compatibility for the synchronous endpoint.
- Add bounded worker concurrency, cancellation, retries, and recovery for interrupted/stale jobs.
- Expose progress events or polling for Retrieving, Generating, Validating, and Ready.
- Let the UI reconnect after refresh and distinguish cancellation from provider failure.
- Document migrations, worker lifecycle, API contracts, and deployment behavior.

Acceptance: integration tests exercise restart/reconnect, duplicate requests, cancellation, bounded retries, and failure persistence. A browser refresh does not lose a generation's identity or status.

## Stage 6 — Review queue and generation presets

Status: implemented. Personal generation presets plus server-backed catalog filters/counts, language/search, 25-row paging and per-account/catalog session preferences. Queue SQL API and browser coverage pass; session filters and URL workspace recovery are verified.

- Add filters for pending approval, missing language, missing facts, and stale content.
- Show useful counts and preserve filters through navigation.
- Add reusable saved generation presets for common workflows.
- Keep model, retrieval mode, facts inclusion, and Top-K accessible under advanced controls.
- Preserve row-level and batch review rules and make bulk actions clear.

Acceptance: queue counts/filters agree with server state; presets round-trip correctly; existing approval/publishing behavior remains covered.

## Stage 7 — Independent lexical retrieval and reranking

Status: implemented. Independent SQL keyword candidates join dense candidates before BM25/RRF and diversification. Candidate counts and retrieval version are audited. A bounded live comparison and optional neural-reranker experiment are recorded. The latter timed out while the baseline already ranked the target first; no neural runtime dependency was enabled.

- Retrieve lexical and dense candidates independently before fusion, rather than limiting lexical ranking to vector candidates.
- Preserve product/document authorization and completed-ingestion constraints in every path.
- Benchmark exact model/SKU/technical terminology, multilingual terms, and missing semantic matches.
- Evaluate an optional local reranker against the existing fusion baseline, with bounded candidate count and timeouts.
- Introduce additional runtime dependencies only when documented evaluation demonstrates an acceptable quality/latency tradeoff.
- Record retrieval version, scores, candidate counts, latency, and selected evidence.

Acceptance: tests prove lexical recall can recover evidence absent from vector results; evaluation reports quality and latency for the proposed default.

## Stage 8 — Graph concepts, document versions, and cache invalidation

Status: implemented within the documented 500-node graph bound. Versioned bilingual terms, typed concept labels, evidence revisions and bounded cache use fresh SQL corpus fingerprints. Historical evidence is retained as a snapshot. Explicit supersession, extraction versions, cache invalidation and failed-ingestion exclusion are verified through live/API tests. The 500-node bound remains; larger-window quality is not claimed.

- Normalize German/English synonyms and distinguish useful entity/concept types.
- Preserve the distinction between co-mentions, adjacency, and verified semantic relations.
- Add document/retrieval revision identifiers and evidence version tracking.
- Cache suitable graph projections with explicit invalidation for upload, deletion, reindexing, and extraction/version changes.
- Audit cache/version information and prevent deleted or superseded evidence from appearing as current.
- Benchmark graph coverage beyond the current bounded window before expanding scope.

Acceptance: isolation and stale-evidence tests pass; synonym cases improve measured coverage; caching does not change authorization or evidence freshness.

## Shared completion criteria

- Every recommendation from the UI/AI review is covered by a stage above.
- Follow the existing .NET layering and reusable React component conventions.
- Add focused tests for substantive behavior, run the applicable build/tests, and document verification limits.
- Check keyboard navigation, focus, readable states, and responsive layouts for changed UI.
- Document API/schema changes, migrations, operational limits, and evaluation outcomes.
- Mark stages complete only when their acceptance criteria are met; record partial progress and the precise next action.

## Checkpoint log

- 2026-11-04: Roadmap documented. New implementation has not started; deferred because current account usage exceeds the user's 40%-consumed threshold.


- 2026-11-04: User cancelled recurring work, then explicitly requested immediate implementation. Added the bounded API evaluation runner, 48 cases / 384 possible combinations, grouped JSON reports and checkpointing. Eleven offline tests pass; dry-run succeeds. No live model calls made. Next: Stage 2 schema-constrained generation or an explicit one-case live smoke evaluation.

- 2026-11-04: Stage 2 implemented following the user's continue request. Added schema-constrained responses, parameter audit and a nullable migration. Live model evaluation remains pending. Next: Stage 3 claim validation and evidence review.
Verification checkpoint: 74 unit tests pass. An earlier adapter/API run passed 20 tests; the final rerun with new parameter-audit assertions was blocked by Windows application control (0x800711C7). Re-run these tests in an allowed environment before rollout. Live schema behavior was exercised in all four modes and a facts-off worker job; see the acceptance report. Weekly usage warning: 98% consumed.

- 2026-11-04: Stage 3 initial slice implemented following continue: claim review and saved evidence with nullable draft migration, API fields, UI, and eight new unit tests. Frontend build passes; .NET test project builds but test execution is blocked by Windows application control. Stage 3 remains partial; next: verify migration/review round trips, then abstention and review safeguards.

- 2026-11-04: Added explicit authenticated warning acknowledgement for approval/publication, individual UI confirmation, batch-approval guard, and tests for snapshot-preserving edits and API persistence. Stage 3 remains partial; abstention/shorter-output behavior is next. Prior Windows application-control test execution block remains a verification limitation.

- 2026-11-04: User explicitly disabled usage checks and requested completion of AI implementation. Added sparse-evidence output/warnings, prompt V4/schema V2, independent SQL lexical recall, bilingual graph concepts, document/chunk/graph revisions, fresh-corpus cache validation, durable jobs/progress/cancellation/recovery and personal presets. Backend/frontend builds pass. Thirteen offline evaluator tests pass; ten focused model-adapter tests pass. Local .NET execution is blocked by Windows application control (0x800711C7); the changed schema-version assertion was updated. Linux CI now includes SQL integration, contract/model and offline evaluator tests. No live-model quality result is claimed; UI-only Stages 4 and queue portion of 6 remain outside the AI completion request.

Final verification checkpoint: solution build succeeds with zero warnings/errors; frontend production build succeeds (existing bundle-size advisory). Thirteen offline evaluator tests, nine claim-review tests and ten structured-generation adapter tests pass. The migration-model runtime check is blocked specifically by Windows application control loading ProductHub.Domain.dll (0x800711C7); broad domain/SQL job and retrieval tests are authored and included in Linux CI but are not claimed passing locally. No live Ollama benchmark, production migration, commit, push or deployment was performed.

- 2026-11-04: Continued with remaining UI Stages 4 and 6. Added bounded same-language saved-version diffs, settings/version labels, dirty-state/navigation guards and background-refresh protection. Added the administrator catalog review-queue API and UI with catalog-wide search/language counts, overlapping filters, paging and session preferences. Queue navigation hydrates products outside the initial 100-row fetch and carries the chosen language. Six comparison tests and three queue tests pass; solution/frontend builds pass. SQL API tests are authored but local domain-dependent execution remains blocked by application control. No live browser checks or deployment performed. See ui-review-workspace.md.

- 2026-11-04 completion audit: closed remaining lifecycle and acceptance gaps: explicit supersession, extraction/chunk policy versions, pending-to-ready ingestion only after vector success, URL reload recovery, persistent lost-submit identities and removal of the legacy root fallback. Current checks: 32 SQL/API integration tests, 18 production-build desktop/mobile browser checks, six token-diff tests, 16 evaluator tests, 17 live API contracts and a live vector-failure check pass. A prior 98-unit-test run passed; the last rebuilt 100-test assembly was blocked by Windows application control. The real-model smoke generated valid JSON in four modes; one facts-only numeric error was correctly flagged, and the stricter offline risk gate now fails it. An optional reranker timed out and was not enabled. No account usage checks, commit, push or production deployment. Full results and conditional quality limits: evaluation-results-2026-11-04.md.
