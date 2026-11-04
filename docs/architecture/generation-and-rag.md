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
2. **Candidate retrieval:** embed queries in one batch. Search Qdrant separately for each vector, filtered to the same product. Each query retrieves `min(TopK * 4, 80)` hits.
3. **SQL verification:** hydrate the union of candidate IDs. Drop stale hits, incomplete documents, and chunks belonging to another product.
4. **Keyword reranking:** calculate BM25 over the semantic candidate union using original query terms. This is candidate reranking, not a full-corpus lexical index; it cannot recover chunks absent from every vector result.
5. **Rank fusion:** combine semantic rankings and the positive-scoring lexical ranking using reciprocal rank fusion, `sum(1 / (60 + rank))`. Normalize by the best fused score for returned `score`.
6. **Diversification:** iteratively choose by `0.8 * normalized relevance - 0.2 * maximum token-set Jaccard similarity` to selected excerpts. Skip candidates with similarity at least `0.85`.
7. **Context budget:** return at most Top-K excerpts, at most 2,400 characters per excerpt, and at most 12,000 excerpt characters total. Citations identify original chunks even when text is truncated.

Top-K is a ceiling. Deduplication and budgeting can reduce result count. Advanced scores are normalized fusion scores; do not compare them directly with naive cosine scores. The character budget covers retrieved text, not the entire prompt or an exact token count.

Retrieval calls are bounded and sequential. Cancellation is forwarded to embeddings, Qdrant, and SQL and checked during excerpt selection. No external reranker, cloud model, or extra infrastructure is required. This implementation does not add LLM query rewriting, cross-encoder reranking, or full-corpus BM25.

## Graph RAG

Graph RAG (API mode `Graph`) starts with advanced semantic retrieval and follows a request-local graph of the selected product's completed SQL document chunks. It can retrieve related passages absent from every vector result.

### Graph construction

- Load at most **500 chunks**, prioritizing semantic seeds, then newer documents, document ID, and chunk ordinal. Both the SQL query and graph builder validate product ownership and completed ingestion status.
- Chunk nodes link to normalized concept terms extracted from their first 2,400 characters. Terms are 4–40 letters/digits/hyphens, lowercased, with a small English/German stopword list. No LLM extracts these concepts.
- Shared concepts create undirected chunk links. Keep terms present in at least two nodes and no more than `min(16, max(2, floor(nodeCount * 0.4)))` nodes, then retain up to 24 rare shared terms per node. A link requires both nodes to retain the term.
- Consecutive ordinals in the same document create `adjacent-chunk` links. Missing ordinals are not bridged.
- Retain up to **12 strongest distinct neighbors** per node. Shared-concept edge weight is `0.7 + min(0.3, sharedConceptCount * 0.05)`; adjacency weight is `0.75`. Ties use chunk ID.

These are co-mention and adjacency relationships, not verified semantic triples or evidence that a product has a capability. The graph is rebuilt from current SQL chunks per request. New and deleted documents are reflected without a separate graph database, graph ingestion job, or reindexing.

### Traversal and context

Reserve up to half the requested output (rounded up, maximum eight) for semantic anchors. Traverse at most **two chunk-to-chunk hops** from those anchors, rejecting cycles and retaining the strongest path to a node. Scores decay by `0.65 * edgeWeight` per hop.

Return anchors first, then connected excerpts by path score, then remaining semantic seeds when space remains. Near-duplicate excerpts are removed at token-set Jaccard similarity `0.85`. The same limits apply: maximum Top-K results, 2,400 characters per excerpt, and 12,000 excerpt characters overall. Top-K 1 returns only the semantic anchor.

The graph projection is bounded: documents outside its 500-node window cannot contribute traversal context. Concepts are heuristic lexical mentions without stemming, entity resolution, or relation extraction. If no links are available, Graph mode returns semantic seeds with zero hops; an empty semantic result still fails generation. A graph/SQL failure aborts generation and does not silently switch modes.

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

Paths are persisted in the existing source-audit JSON and displayed in **Model audit → Evidence → Graph paths**. Graph scores describe relevance/path weighting, not factual confidence. Non-graph historical sources deserialize with null graph fields. Only cited excerpt text reaches generation; traversal labels are audit metadata and never grant instructions authority.

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

The standalone `POST /api/knowledge/search` endpoint retains its existing naive-search contract. Generation selects advanced and graph retrieval through the generation endpoint.

## Persistence and rollout

Migration `20261103120000_AddGenerationFactsSelection` adds `AiGenerations.IncludeFacts` and backfills historical runs with `true`. Retrieval mode uses existing string-valued `GroundingMode`; historical `Rag` rows represent naive RAG. Product facts and documents remain unchanged.

Default generation prompt is `product-description-v3`. Earlier embedded versions remain available for historical audit and explicitly configured use. V3 handles excluded attributes and identity-only generation; reference material remains untrusted input.

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
