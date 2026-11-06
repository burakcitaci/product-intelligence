# Generation sources and retrieval modes

Generation has two independent choices: whether to include structured product attributes, and whether/how to retrieve uploaded reference material. The product name, brand, and SKU are always included to identify the item.

## UI workflow

1. Open a product and choose **New draft** or **Generate**.
2. Choose the draft language and local generation model.
3. Enable or disable **Include product facts**.
4. Choose **No RAG**, **Naive RAG**, **Advanced RAG**, or **Graph RAG**.
5. For RAG, upload product knowledge and wait for indexing to complete. Choose the maximum excerpts, from 1 to 20 (default 5).
6. Generate and review. The model audit shows retrieval mode, facts inclusion, requested Top-K, evidence, timings, and model.

Failed or pending documents do not enable RAG generation. The API also checks evidence and never silently changes a RAG request into facts-only generation.

| Include facts | Retrieval | Model input |
| --- | --- | --- |
| Yes | None | Identity and structured attributes |
| No | None | Identity only; do not invent missing specifications |
| Yes | Naive | Identity, attributes, and one semantic search's excerpts |
| No | Naive | Identity and semantic excerpts; attributes excluded |
| Yes | Advanced | Identity, attributes, and fused/diversified excerpts |
| No | Advanced | Identity and fused/diversified excerpts; attributes excluded |
| Yes | Graph | Identity, attributes, semantic anchors, and graph-linked passages |
| No | Graph | Identity and graph-linked passages; attributes excluded |

Disabling facts removes attributes from both embedding queries and generation context. It does not redact information already in an uploaded document. Retrieved instructions remain advisory guidance: they cannot establish product specifications, capabilities, certifications, or included accessories.

## Naive RAG

`KnowledgeSearchService` embeds one query containing product identity and, when enabled, attributes. Qdrant returns the requested Top-K cosine-similarity hits filtered to the product. SQL hydrates chunks and validates completed document status and product ownership. Results preserve vector ranking and cosine similarity as `score`.

## Advanced RAG

The same ingestion pipeline, embeddings model, SQL chunks, and Qdrant collection are reused. Existing documents do not need reindexing.

1. **Query expansion:** up to three distinct queries from supplied terms: the full query, the first three identity clauses, and name/brand clauses. Expansion is deterministic, with no additional LLM call or invented facts.
2. **Independent candidate retrieval:** embed queries in one batch. Search Qdrant separately for each vector, filtered to the same product, with `min(TopK * 4, 80)` hits each. Independently search completed SQL chunks for up to 24 supplied/normalized vocabulary terms. SQL ranks term presence before limiting to 80 candidates; it can recover passages absent from all vector results.
3. **SQL verification:** combine vector IDs and independent lexical candidates, then hydrate and validate SQL evidence. Drop stale hits, incomplete documents, and chunks belonging to another product.
4. **Keyword reranking:** calculate BM25 over the independent dense/lexical candidate union using the versioned English/German vocabulary. Exact model identifiers preserve embedded hyphens and dots. SQL candidate selection uses term-presence ranking; BM25 statistics are calculated on the candidate union, not the full corpus.
5. **Rank fusion:** combine semantic rankings and the positive-scoring lexical ranking using reciprocal rank fusion, `sum(1 / (60 + rank))`. Normalize by the best fused score for returned `score`.
6. **Diversification:** iteratively choose by `0.8 * normalized relevance - 0.2 * maximum token-set Jaccard similarity` to selected excerpts. Skip candidates with similarity at least `0.85`.
7. **Context budget:** return at most Top-K excerpts, at most 2,400 characters per excerpt, and at most 12,000 excerpt characters total. Citations identify original chunks even when text is truncated.

Top-K is a ceiling. Deduplication and budgeting can reduce result count. Advanced scores are normalized fusion scores; do not compare them directly with naive cosine scores. The character budget covers retrieved text, not the entire prompt or an exact token count.

Retrieval calls are bounded and sequential. Cancellation is forwarded to embeddings, Qdrant, and SQL and checked during excerpt selection. No external reranker, cloud model, or extra infrastructure is required. SQL keyword retrieval currently scans matching text rather than requiring a SQL full-text index. Candidate results are bounded; database scan cost still grows with corpus size. No optional neural reranker is enabled without measured quality/latency evidence. BM25 plus fusion remains the deterministic reranking baseline.

## Graph RAG

Graph RAG (API mode `Graph`) starts with advanced hybrid retrieval and follows a bounded cached graph of the selected product's completed SQL document chunks. It can retrieve related passages absent from every vector result.

### Graph construction

- Load at most **500 chunks**, prioritizing semantic seeds, then newer documents, document ID, and chunk ordinal. Both the SQL query and graph builder validate product ownership and completed ingestion status.
- Chunk nodes link to normalized concept terms extracted from their first 2,400 characters. Terms are 4–40 characters, lowercased, with a small English/German stopword list and an explicitly versioned bilingual synonym map. Guidance, attribute, material and product-term labels aid inspection; other concepts remain co-mentions. No LLM extracts these concepts.
- Shared concepts create undirected chunk links. Keep terms present in at least two nodes and no more than `min(16, max(2, floor(nodeCount * 0.4)))` nodes, then retain up to 24 rare shared terms per node. A link requires both nodes to retain the term.
- Consecutive ordinals in the same document create `adjacent-chunk` links. Missing ordinals are not bridged.
- Retain up to **12 strongest distinct neighbors** per node. Shared-concept edge weight is `0.7 + min(0.3, sharedConceptCount * 0.05)`; adjacency weight is `0.75`. Ties use chunk ID.

These are co-mention and adjacency relationships, not verified semantic triples or evidence that a product has a capability. SQL corpus membership is refreshed on every request. A SHA-256 revision covers algorithm/vocabulary version, product ID, chunk IDs, ordinals, document IDs, file hashes and chunk text. The cached graph is used only for that exact revision. New and deleted documents are reflected without a separate graph database, graph ingestion job, or reindexing.

### Traversal and context

Reserve up to half the requested output (rounded up, maximum eight) for semantic anchors. Traverse at most **two chunk-to-chunk hops** from those anchors, rejecting cycles and retaining the strongest path to a node. Scores decay by `0.65 * edgeWeight` per hop.

Return anchors first, then connected excerpts by path score, then remaining semantic seeds when space remains. Near-duplicate excerpts are removed at token-set Jaccard similarity `0.85`. The same limits apply: maximum Top-K results, 2,400 characters per excerpt, and 12,000 excerpt characters overall. Top-K 1 returns only the semantic anchor.

The graph projection is bounded: documents outside its 500-node window cannot contribute traversal context. Concepts are heuristic lexical mentions without stemming, entity resolution, or relation extraction. If no links are available, Graph mode returns semantic seeds with zero hops; an empty hybrid result still fails generation. A graph/SQL failure aborts generation and does not silently switch modes.

This is lightweight local graph retrieval. It does not implement Microsoft's full GraphRAG indexing, extracted entity relationships, community summaries, global search, or DRIFT search. See [Microsoft GraphRAG local search](https://microsoft.github.io/graphrag/query/local_search/) for that system's richer context model.

### Auditing graph paths

Each source adds optional `graphHops` and `graphPath` fields. Seeds have zero hops and a one-citation path; graph expansions record the root citation, edge labels, and reached citations:

```json
{
  "citation": "cleaning.md#chunk-0",
  "graphHops": 2,
  "graphPath": [
    "control.md#chunk-0",
    "shared-concept:thermostat",
    "care.md#chunk-0",
    "shared-concept:descaling",
    "cleaning.md#chunk-0"
  ]
}
```

Paths are persisted in the existing source-audit JSON and displayed in **Model audit â†’ Evidence â†’ Graph paths**. Graph scores describe relevance/path weighting, not factual confidence. Non-graph historical sources deserialize with null graph fields. Only cited excerpt text reaches generation; traversal labels are audit metadata and never grant instructions authority.

## Generation API

`POST /api/products/{productId}/drafts/generate` requires an administrator session and `Idempotency-Key` header:

```json
{
  "language": "en",
  "model": "qwen3:8b",
  "includeFacts": false,
  "retrievalMode": "Advanced",
  "topK": 5
}
```

`retrievalMode` accepts `None`, `Naive`, `Advanced`, or `Graph` (case-insensitive). `includeFacts` defaults to `true`. If `retrievalMode` is omitted, the legacy `useRag` flag selects `Naive` when true and `None` when false. An explicit mode takes precedence. Invalid modes and RAG Top-K outside 1–20 return HTTP 400.

Success returns HTTP 201, the saved draft, and `Location`. Empty retrieval produces HTTP 503 with `rag-no-context`; retrieval infrastructure failure produces `rag-retrieval-failed`. Neither calls the generation model. Existing idempotency semantics remain: use a new key when intentionally changing settings.

`GET /api/drafts/{id}/generation` adds `includeFacts` and `retrievalMode`. Historical `groundingMode` remains: `FactsOnly`, `IdentityOnly`, `Rag`, `AdvancedRag`, or `GraphRag`. `requestedTopK` is null without retrieval. Citations, counts, retrieval status, latency, and failure codes remain audited.

The standalone `POST /api/knowledge/search` endpoint defaults to Naive for compatibility and now accepts optional string mode: Naive, Advanced, or Graph. Graph requires productId. Unknown modes are rejected. This also supports bounded retrieval-only acceptance experiments.

## Persistence and rollout

Migration `20261103120000_AddGenerationFactsSelection` adds `AiGenerations.IncludeFacts` and backfills historical runs with `true`. Retrieval mode uses existing string-valued `GroundingMode`; historical `Rag` rows represent naive RAG. Product facts and documents remain unchanged.

Default generation prompt is `product-description-v4`. Earlier embedded versions remain available for historical audit and explicitly configured use. V4 permits zero to five highlights and short identity-only content when evidence is sparse; reference material remains untrusted input.

Development startup applies migrations through the existing seeder. For other deployments, run from the repository root before serving requests:

```bash
dotnet tool restore
dotnet ef database update --project src/ProductHub.Infrastructure --startup-project src/ProductHub.Api
```

The migration is included in source; editing application files does not update a running database. Rebuild and restart the API/frontend using the normal project launcher after applying migrations. Production never receives sample data automatically.

Graph RAG adds no schema migration: its mode uses the existing string-valued grounding column, and graph provenance uses the existing source JSON. The earlier facts-selection migration must still be applied when upgrading from an older release.

## Verification

Unit tests cover all eight combinations, legacy compatibility, explicit mode precedence, excluded facts, source isolation, stale hits, retrieval failure, query expansion, deduplication, candidate/context limits, and cancellation.

API integration tests use fake generation/embedding/vector adapters with an isolated SQL Server Testcontainer. They check all eight options through HTTP, saved metadata, migration/model consistency, and validation without depending on Ollama or Qdrant.

Graph-specific tests cover passages missing from vector results, shared-concept and adjacency paths, two-hop limits, cycles, cross-product/incomplete-document isolation, deleted seeds, bounded corpus/context, and cancellation. SQL-backed API tests verify two-hop provenance survives persistence and HTTP serialization.

## Schema-constrained generation

The adapter now supplies the `product-content-v2` JSON schema to local Ollama. Required fields are title, shortDescription, longDescription, and highlights; extra properties are disallowed. Length and highlight-count bounds match a shared application validator, which additionally rejects whitespace-only fields.

Malformed JSON, missing fields, duplicate properties, and extra properties fail as `invalid-json`. Parsed content outside application bounds fails as `invalid-output`. Invalid output is not retried or silently repaired. Transient connection failures retain the two-attempt maximum within one timeout; caller cancellation is preserved.

The `Ai` configuration explicitly sets `Temperature` (default 0), `Seed` (default 42; nullable), and `MaxOutputTokens` (default 2048). Startup validates temperature 0–2 and output tokens 128–16384. Fixed seeds do not guarantee provider determinism. These settings concern product generation, not the coding agent's reasoning effort.

Before inference, schema version and generation parameters are saved in `AiGenerations.GenerationParametersJson`. The generation metadata API exposes this snapshot as `parameters`. Historical records return null; the evaluation runner retains new metadata in its reports.

Apply migration `20261104100000_AddGenerationParameters` before serving the updated API. Development startup applies pending migrations; other deployments should run:

```bash
dotnet ef database update --project src/ProductHub.Infrastructure --startup-project src/ProductHub.Api
```

Existing drafts and request fields remain compatible. Schema conformance does not establish factual support. Adapter tests use a fake chat client; live Ollama behavior and output quality still require an explicit smoke evaluation.

## Claim review and saved evidence

New drafts save a versioned review with identity, included facts, and retrieved excerpt snapshots. Each title, description sentence, and highlight has a stable content/field identifier, a status, an explanation, and linked evidence. The product detail screen exposes these as expandable claims. Historical drafts have no review and are displayed as such.

Checks are intentionally conservative:
- Supported: the whole normalized claim exactly matches a supplied identity/fact or its label/value representation.
- Conflict: a detected numeric value differs from all supplied quantities with the same recognized unit. Context and conversions still need human review.
- Unsupported: a quantity or selected certification/compatibility/accessory/performance wording lacks a whole-claim exact match.
- Advisory: recommendation wording exactly matches a saved reference passage, without establishing specifications.
- Unverified: no deterministic conclusion about the complete claim.

Partial fact mentions can link evidence without verifying the surrounding sentence. These heuristics do not perform semantic entailment, general certification extraction, unit conversion, or reliable negation analysis. Flags can be false positives; absence of a flag is not proof of truth. Human review remains required.

Edits are reassessed using the original saved context rather than silently substituting today's product facts. Evidence is a historical snapshot, not a claim of current validity. Legacy drafts are not automatically re-grounded. Reviews and citations remain separate from published copy.

Migration 20261104110000_AddDraftClaimReview adds nullable ContentDrafts.ClaimReviewJson. Apply it after the parameter-audit migration before serving the updated API. Development startup applies pending migrations.

Sparse output, explicit warnings and authenticated acknowledgement gates are implemented below. Backend/frontend builds, the 32-test integration suite, 17 live API checks and 18 browser checks pass. Windows application control (0x800711C7) blocked the final rebuilt unit-test binary; live model smoke results are documented separately. The deterministic checker intentionally does not promise general semantic verification.

### Approval acknowledgement safeguard

New drafts with any non-Supported review result or insufficient-evidence warning require explicit warning acknowledgement for approval. POST to the approve endpoint with JSON { "acknowledgeWarnings": true } after inspecting the evidence. Without it, the API returns HTTP 409. Reviewer identity comes from authentication, never from request fields; the saved review records identity and UTC timestamp.

The product UI asks for this acknowledgement for individual approval. Bulk approval refuses flagged drafts and directs users to individual review. Publishing an approved draft also rejects unacknowledged review warnings. Reassessment after edits produces an unacknowledged review using the original evidence snapshot. This acknowledgement is a human attestation, not automated proof of correctness.

Historical drafts without saved review retain their earlier API behavior; missing evidence is displayed rather than retroactively declared verified. Existing API clients approving new flagged drafts must supply the acknowledgement body. No additional migration is needed beyond the claim-review column.

Tests were added for acknowledgement gates, identity/timestamp recording, original-evidence edits, and persisted API reviews. These review round trips passed in the SQL integration suite and live acceptance API. The claim-review and model-adapter tests can also run separately without live model calls.


## Sparse evidence and abstention

The product-content-v2 schema accepts zero to five highlights. Every supplied item must still be non-empty and within 300 characters; title and descriptions remain required and bounded. The domain, generation validator, editing endpoint, UI and evaluation runner all accept an empty array. No minimum count forces the model to invent claims. Prompt V4 explicitly requests shorter descriptions and omission of unsupported highlights. An empty RAG result remains an explicit rag-no-context error, rather than silently changing the requested retrieval mode.

Claim-review-v2 records insufficient-specification warnings when facts are excluded or absent and a warning when no highlights were produced. These warnings require human acknowledgement even if every remaining claim exactly matches identity. This is evidence-aware abstention/review assistance, not a calibrated confidence score or a semantic truth guarantee. The UI includes all saved reference passages for manual review even when a claim cannot be linked automatically.

## Evidence versions and graph cache

Every selected source audits document SHA-256, a full SHA-256 chunk revision, retrieval algorithm/vocabulary version, dense and lexical candidate counts. Graph results additionally record graph revision, cache hit/miss, typed lexical concepts, hops and paths. Saved GroundingExcerpt snapshots preserve document/chunk IDs and revisions alongside the exact excerpt used for generation. Historical rows deserialize with absent revision fields; they are labelled legacy rather than assigned invented versions.

Graph projections are cached process-locally for at most ten minutes and 32 revisions. A fresh authorized SQL corpus is always read before lookup, so deletion, replacement uploads, chunk changes/reindexing and vocabulary changes produce a different projection revision. The cache never supplies old chunk entities; output is mapped from the current SQL projection. Removing a document invalidates its retrieval membership while historical draft evidence remains an explicit snapshot. A new upload is a distinct document revision. Administrators can explicitly supersede an older completed source with a newer completed active document from the same product using POST /api/products/{productId}/documents/{id}/supersede and replacementDocumentId. The UI exposes this after upload. Superseded sources are excluded from naive hydration, SQL lexical candidates and graph projections; historical chunks and saved draft evidence remain intact. Replaying the same replacement is idempotent; older replacements, cycles, cross-product references and concurrent conflicting changes are rejected. No implicit supersession occurs by filename. Deleting a replacement does not reactivate its older sources.

The graph still has a 500-node window and two-hop bound. Cache hits do not expand that coverage or prove semantic relationships. Quality/latency improvements need the bounded real-model benchmark; no benchmark result is asserted by unit tests.

## Durable UI generation and presets

The UI now uses durable generation jobs; the synchronous endpoint remains compatible. See [Generation jobs](generation-jobs.md) for states, APIs, cancellation, leases, restart recovery, migration and operational limits. Personal generation presets store model, retrieval mode, facts selection and Top-K per authenticated account in this browser (up to eight named presets). Built-in facts-only, hybrid and graph presets retain the currently selected model. Presets do not change server authorization, model allowlists or evidence validation.


## Extraction and indexing lifecycle

Migration 20261104130000_AddDocumentRevisions adds extraction version and supersession metadata. Existing rows are labelled legacy. New ingestion records the local extractor/chunk-policy version and configured chunk size/overlap; the chunk fingerprint includes that extraction version.

Ingestion prepares chunks while the document is Pending, writes vectors, then marks the document Completed. A failed vector write leaves Failed status, even if some chunks or vector points were prepared. SQL-authoritative readiness filters exclude those chunks from every retrieval path. A live fault-injection check verified HTTP 503 plus persisted Failed status with prepared chunks.

Current acceptance and measured model limitations are documented in [Acceptance results](../evaluation-results-2026-11-04.md).
