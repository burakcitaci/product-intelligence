# M1 data model

## Products

`Products` stores a GUID primary key, unique normalized SKU, name, brand, optional future category identifier, timestamps, and a SQL Server `rowversion` concurrency token. A `(Brand, Name)` index supports common catalog browsing.

Attributes are serialized as JSON in `nvarchar(max)`. This is pragmatic for M1 and preserves variable supplier data without prematurely designing an attribute-definition subsystem. The tradeoff is intentionally limited server-side filtering and indexing inside attributes. Promote frequently queried attributes to relational columns or a normalized model when concrete query requirements emerge.

The domain rejects blank core fields and duplicate attribute names. Database constraints and the unique SKU index provide the final consistency boundary under concurrent requests.

## AI generations

`AiGenerations` records provider, model, prompt version, language, timings, token counts, status, error code, correlation id, and the caller's idempotency key. `(ProductId, IdempotencyKey)` is unique. Token counts remain nullable because not every provider/model response supplies usage.

Grounding is audited by `GroundingMode` (FactsOnly, IdentityOnly, Rag, AdvancedRag, GraphRag), `IncludeFacts`, requested Top-K, retrieval status/duration, chunk/document counts, source citations/scores, and retrieval error code. The API exposes independent `retrievalMode` as None, Naive, Advanced, or Graph. Historical Rag runs map to Naive.

Migration `20261103120000_AddGenerationFactsSelection` adds the boolean facts flag with a true backfill. New requests can persist false without changing authoritative product attributes. Advanced retrieval reuses existing chunks and vector points, without reindexing. See [Generation and RAG](generation-and-rag.md).

Graph retrieval uses a bounded versioned cached projection of existing completed chunks; no graph tables are added. Graph sources persist optional `GraphHops` and `GraphPath` in the existing source-audit JSON. Historical source records retain null graph fields. No additional migration or reindexing is needed for Graph mode.

The nullable GenerationParametersJson column stores schema version, temperature, seed, and output-token limit before inference. Migration 20261104100000_AddGenerationParameters adds it; historical settings remain unknown. The generation API exposes this snapshot as parameters.

## Content drafts

`ContentDrafts` belongs to a product and exactly one generation. It stores localized title, descriptions, JSON highlights, and a review status. New drafts always start as `PendingReview`; generation never publishes or overwrites catalog content. Highlights use JSON because they are a small ordered value collection and are not queried independently in M2.

## Knowledge documents and chunks

`KnowledgeDocuments` stores file identity, SHA-256 deduplication, ingestion status, timestamps, and failure code. `DocumentChunks` stores ordered normalized text and cascades with its document. The unique `(DocumentId, Ordinal)` index preserves deterministic chunk order.

Qdrant points use `DocumentChunk.Id` as their point ID. Embeddings are intentionally absent from SQL Server and can be regenerated from chunk text.

## Agent workflows

`AgentWorkflows` records the product, objective, status, validated plan, tool results, optional mutation proposal, error code, and timestamps. Plans and results use JSON because their schemas are bounded application contracts and are read as an audit record rather than queried relationally.

Terminal states are `Completed`, `Rejected`, and `Failed`. `AwaitingApproval` always contains a proposal; product changes occur only when approval transitions it to `Completed`.


## Generation jobs

Migration 20261104120000_AddGenerationJobs creates the durable GenerationJobs queue. Product/key is unique; command JSON preserves requested settings and requester identity. Status/stage, lease token/expiry, cancellation flag, draft result and safe error fields support reconnect and interrupted-worker recovery. Conditional SQL updates fence worker claims and final results. Jobs cascade when a product is deleted. See [job lifecycle](generation-jobs.md).

Source audit JSON now includes document and chunk revisions, retrieval version, candidate counts and graph cache provenance without a new source table. Grounding snapshots retain source IDs/revisions for historical claim review. ContentDraft.ClaimReviewJson (migration 20261104110000_AddDraftClaimReview) also stores sparse-evidence warnings and authenticated warning acknowledgement. Old snapshots remain nullable. Highlights may contain zero to five items; the existing JSON storage needs no migration for this relaxed count.


Migration 20261104130000_AddDocumentRevisions adds KnowledgeDocuments.ExtractionVersion, SupersededByDocumentId and SupersededAt. The replacement ID is retained as historical metadata, with optimistic concurrency on supersession. No cascading relation reactivates or deletes old evidence when a replacement is removed. New retrieval accepts only completed, non-superseded sources. Chunk fingerprints include the extraction policy version.
