# UI and AI implementation roadmap

Status: planned; implementation is deferred until the user's Codex usage allows a bounded stage.
Requested: 2026-11-04.

This roadmap records all improvements approved by the user after the UI/AI review. It is a work plan, not a claim that the features below are already implemented.

## Execution and usage policy

- Work in small, independently testable stages, in the order below. Update this file and the relevant README/architecture documentation after each completed stage.
- Before starting a stage, after substantial implementation/checks, and before beginning another stage, read account usage through the available Codex usage-limits tool. Prefer its per-limit buckets.
- Interpret the user's warning threshold as **40% consumed**, not 40% remaining, in any reported account usage window.
- If any available window is at or above 40%, warn the user, record a safe checkpoint, and defer additional implementation. If usage cannot be read, report that limitation and defer the expensive work.
- Resume a deferred stage only after a fresh usage check shows ordinary usage is permitted and all reported windows are below the threshold. Missing percentages are unknown, not zero.
- These checks are periodic checkpoints, not a continuous account-wide alarm or a guarantee that concurrent activity cannot cross the threshold between checks.
- Do not purchase credits, redeem reset credits, or change the user's model/reasoning settings automatically. Respect the user's selected settings.
- Preserve the existing uncommitted Graph RAG work. Do not auto-commit, push, or deploy this roadmap's changes.
- Keep model-backed evaluation runs explicit and bounded; do not run the full model/mode matrix repeatedly during routine development.
- A scheduled follow-up should recheck eligibility, complete a coherent stage if possible, save progress, and stop at the threshold. Disable the follow-up when all stages are complete.

## Existing baseline

The project already has a compact review workspace, facts inclusion controls, None/Naive/Advanced/Graph retrieval modes, and generation/source auditing. Graph RAG currently uses a bounded request-local lexical chunk/concept graph. The most recent recorded validation was 74 unit tests, 13 generation API integration tests, and a successful frontend build; those checks do not establish live model output quality.

## Stage 1 — Real-model evaluation runner

Status: not started.

- Expand the small retrieval baseline into approximately 40–60 representative German/English cases covering product identity, units, exact model names, instructions, conflicting documents, missing evidence, and prompt injection attempts.
- Compare the four retrieval modes with facts included and excluded on stable input snapshots.
- Add a local evaluation command with case/mode/model filters, dry-run support, bounded run size, cancellation, and machine-readable reports.
- Measure retrieval recall, supported/unsupported claims, factual coverage, JSON validity, latency, tokens, and human editorial acceptance where applicable.
- Separate deterministic checks from human or optional model judging; do not label a self-reported model score as verified confidence.
- Document how to run a small smoke evaluation and a full benchmark, including model/version and hardware context.

Acceptance: deterministic evaluator tests pass; a documented bounded real-model run can produce reproducible reports without being required for every build.

## Stage 2 — Schema-constrained generation

Status: not started. Depends on Stage 1's reporting baseline.

- Supply the exact generated-content JSON schema through the local Ollama adapter, retaining server-side validation.
- Explicitly configure and audit generation parameters that affect reproducibility.
- Handle schema/validation failures with bounded, observable recovery; preserve cancellation, idempotency, and failure auditing.
- Maintain compatibility for existing saved drafts and documented API clients.

Acceptance: adapter/contract tests verify the schema and error paths; benchmark results distinguish malformed output from unsupported content.

## Stage 3 — Claim validation, abstention, and evidence review

Status: not started. Depends on Stage 2.

- Add stable claim/evidence identifiers and saved evidence snapshots suitable for review.
- Check numeric values, units, certifications, compatibility, accessories, and other high-impact specifications against authoritative facts.
- Detect missing or conflicting support and distinguish verified facts from advisory document guidance.
- Allow insufficient evidence to produce shorter content or an explicit review warning; do not force invented highlights to satisfy formatting.
- Link sentences/highlights to supporting facts or document excerpts in the review UI.
- Clearly mark unsupported, conflicting, advisory, and unverified content; clicking a claim opens its exact supporting passage.
- Keep citations and validation metadata separate from published marketing copy.

Acceptance: tests cover supported, unsupported, conflicting, and insufficient-evidence cases, including facts-off modes. Reviewers can inspect evidence without relying on a bare similarity score.

## Stage 4 — Draft comparison

Status: not started. Can follow Stage 3.

- Compare the selected draft with the current published version or another saved version.
- Highlight changes to titles, descriptions, highlights, and factual values; identify language/version/settings.
- Keep the published content visible while a replacement is being generated.
- Make unsaved edits and navigation loss clear.

Acceptance: meaningful difference cases and keyboard behavior are verified; comparing drafts never mutates or publishes them.

## Stage 5 — Durable generation jobs and progress

Status: not started.

- Move long-running retrieval/generation into durable jobs with explicit queued/running/stage/success/failure/cancelled states.
- Preserve existing generation idempotency and define backward compatibility for the synchronous endpoint.
- Add bounded worker concurrency, cancellation, retries, and recovery for interrupted/stale jobs.
- Expose progress events or polling for Retrieving, Generating, Validating, and Ready.
- Let the UI reconnect after refresh and distinguish cancellation from provider failure.
- Document migrations, worker lifecycle, API contracts, and deployment behavior.

Acceptance: integration tests exercise restart/reconnect, duplicate requests, cancellation, bounded retries, and failure persistence. A browser refresh does not lose a generation's identity or status.

## Stage 6 — Review queue and generation presets

Status: not started. Builds on stable validation/job metadata.

- Add filters for pending approval, missing language, missing facts, and stale content.
- Show useful counts and preserve filters through navigation.
- Add reusable saved generation presets for common workflows.
- Keep model, retrieval mode, facts inclusion, and Top-K accessible under advanced controls.
- Preserve row-level and batch review rules and make bulk actions clear.

Acceptance: queue counts/filters agree with server state; presets round-trip correctly; existing approval/publishing behavior remains covered.

## Stage 7 — Independent lexical retrieval and reranking

Status: not started. Requires Stage 1 evaluation.

- Retrieve lexical and dense candidates independently before fusion, rather than limiting lexical ranking to vector candidates.
- Preserve product/document authorization and completed-ingestion constraints in every path.
- Benchmark exact model/SKU/technical terminology, multilingual terms, and missing semantic matches.
- Evaluate an optional local reranker against the existing fusion baseline, with bounded candidate count and timeouts.
- Introduce additional runtime dependencies only when documented evaluation demonstrates an acceptable quality/latency tradeoff.
- Record retrieval version, scores, candidate counts, latency, and selected evidence.

Acceptance: tests prove lexical recall can recover evidence absent from vector results; evaluation reports quality and latency for the proposed default.

## Stage 8 — Graph concepts, document versions, and cache invalidation

Status: not started. Requires Stage 1 evaluation.

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

