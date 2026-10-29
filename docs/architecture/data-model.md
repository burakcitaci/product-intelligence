# M1 data model

## Products

`Products` stores a GUID primary key, unique normalized SKU, name, brand, optional future category identifier, timestamps, and a SQL Server `rowversion` concurrency token. A `(Brand, Name)` index supports common catalog browsing.

Attributes are serialized as JSON in `nvarchar(max)`. This is pragmatic for M1 and preserves variable supplier data without prematurely designing an attribute-definition subsystem. The tradeoff is intentionally limited server-side filtering and indexing inside attributes. Promote frequently queried attributes to relational columns or a normalized model when concrete query requirements emerge.

The domain rejects blank core fields and duplicate attribute names. Database constraints and the unique SKU index provide the final consistency boundary under concurrent requests.

## AI generations

`AiGenerations` records provider, model, prompt version, language, timings, token counts, status, error code, correlation id, and the caller's idempotency key. `(ProductId, IdempotencyKey)` is unique. Token counts remain nullable because not every provider/model response supplies usage.

## Content drafts

`ContentDrafts` belongs to a product and exactly one generation. It stores localized title, descriptions, JSON highlights, and a review status. New drafts always start as `PendingReview`; generation never publishes or overwrites catalog content. Highlights use JSON because they are a small ordered value collection and are not queried independently in M2.

## Knowledge documents and chunks

`KnowledgeDocuments` stores file identity, SHA-256 deduplication, ingestion status, timestamps, and failure code. `DocumentChunks` stores ordered normalized text and cascades with its document. The unique `(DocumentId, Ordinal)` index preserves deterministic chunk order.

Qdrant points use `DocumentChunk.Id` as their point ID. Embeddings are intentionally absent from SQL Server and can be regenerated from chunk text.

## Agent workflows

`AgentWorkflows` records the product, objective, status, validated plan, tool results, optional mutation proposal, error code, and timestamps. Plans and results use JSON because their schemas are bounded application contracts and are read as an audit record rather than queried relationally.

Terminal states are `Completed`, `Rejected`, and `Failed`. `AwaitingApproval` always contains a proposal; product changes occur only when approval transitions it to `Completed`.
