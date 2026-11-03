# Generation sources and retrieval modes

Generation has two independent choices: whether to include structured product attributes, and whether/how to retrieve uploaded reference material. The product name, brand, and SKU are always included to identify the item.

## UI workflow

1. Open a product and choose **New draft** or **Generate**.
2. Choose the draft language and local generation model.
3. Enable or disable **Include product facts**.
4. Choose **No RAG**, **Naive RAG**, or **Advanced RAG**.
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

`retrievalMode` accepts `None`, `Naive`, or `Advanced` (case-insensitive). `includeFacts` defaults to `true`. If `retrievalMode` is omitted, the legacy `useRag` flag selects `Naive` when true and `None` when false. An explicit mode takes precedence. Invalid modes and RAG Top-K outside 1–20 return HTTP 400.

Success returns HTTP 201, the saved draft, and `Location`. Empty retrieval produces HTTP 503 with `rag-no-context`; retrieval infrastructure failure produces `rag-retrieval-failed`. Neither calls the generation model. Existing idempotency semantics remain: use a new key when intentionally changing settings.

`GET /api/drafts/{id}/generation` adds `includeFacts` and `retrievalMode`. Historical `groundingMode` remains: `FactsOnly`, `IdentityOnly`, `Rag`, or `AdvancedRag`. `requestedTopK` is null without retrieval. Citations, counts, retrieval status, latency, and failure codes remain audited.

The standalone `POST /api/knowledge/search` endpoint retains its existing naive-search contract. Generation selects advanced retrieval through the generation endpoint.

## Persistence and rollout

Migration `20261103120000_AddGenerationFactsSelection` adds `AiGenerations.IncludeFacts` and backfills historical runs with `true`. Retrieval mode uses existing string-valued `GroundingMode`; historical `Rag` rows represent naive RAG. Product facts and documents remain unchanged.

Default generation prompt is `product-description-v3`. Earlier embedded versions remain available for historical audit and explicitly configured use. V3 handles excluded attributes and identity-only generation; reference material remains untrusted input.

Development startup applies migrations through the existing seeder. For other deployments, run from the repository root before serving requests:

```bash
dotnet tool restore
dotnet ef database update --project src/ProductHub.Infrastructure --startup-project src/ProductHub.Api
```

The migration is included in source; editing application files does not update a running database. Rebuild and restart the API/frontend using the normal project launcher after applying migrations. Production never receives sample data automatically.

## Verification

Unit tests cover all six combinations, legacy compatibility, explicit mode precedence, excluded facts, source isolation, stale hits, retrieval failure, query expansion, deduplication, candidate/context limits, and cancellation.

API integration tests use fake generation/embedding/vector adapters with an isolated SQL Server Testcontainer. They check all six options through HTTP, saved metadata, migration/model consistency, and validation without depending on Ollama or Qdrant.
