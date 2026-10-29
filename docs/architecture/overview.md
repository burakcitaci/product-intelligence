# Architecture overview

Product Intelligence Hub is a modular monolith with one deployable ASP.NET Core API.

- **Domain** owns product invariants and has no infrastructure dependencies.
- **Application** orchestrates product use cases through explicit persistence ports.
- **Infrastructure** implements those ports with EF Core and SQL Server.
- **API** is the composition root and translates HTTP contracts to application commands.

Feature-oriented folders are preferred inside each layer. M2 adds a provider-neutral `IContentGenerator` in Application and an Ollama-backed `IChatClient` adapter in Infrastructure. Application orchestration never references Ollama.

Requests do not expose EF/domain entities. Errors use RFC 9457-compatible Problem Details. Collection reads are bounded to 100 records per page.

## Generation consistency

The generation record is committed before the remote model call, so no SQL transaction remains open during inference. After successful inference, the draft and completed metadata are committed together by one EF `SaveChanges` transaction. Failures update the existing generation record where possible.

A unique `(ProductId, IdempotencyKey)` index serializes repeated requests. A completed request returns its existing draft; an in-progress or failed request returns conflict. Process termination can leave an `InProgress` row, deliberately favoring duplicate prevention over automatic replay. Operational recovery is manual in M2; a later background worker may reconcile stale rows.

## M3 retrieval flow

`POST /api/documents` extracts local text or PDF content, normalizes it into overlapping chunks, requests embeddings from Ollama, stores vectors in Qdrant, and persists document/chunk metadata in SQL Server. `POST /api/knowledge/search` embeds the query, performs cosine similarity search, joins vector hits to SQL chunks, and returns ranked text with stable source citations.

The Application layer owns orchestration and provider-neutral contracts. Infrastructure owns PDF extraction and the Ollama/Qdrant HTTP adapters. SQL Server is authoritative; Qdrant is a rebuildable projection.

## M4 agent workflow

An agent request creates a durable `Running` workflow before model inference. Ollama returns a structured sequence of tool calls. Application code validates tool names, step count, arguments, and execution time before dispatching read-only product lookup or validation tools.

`product.propose_update` stores a proposal and transitions the workflow to `AwaitingApproval`; it does not mutate the product. Separate approve and reject endpoints make the human decision explicit and auditable. Approval reloads the tracked product, applies the domain update, and completes the workflow in one SQL transaction.

## M5 review workspace

The Vite/React client is a separate local process and consumes only HTTP application contracts. It never reads persistence entities or calls Ollama directly. Product selection loads saved drafts and generation metadata; draft creation remains an API operation protected by an idempotency key.

Approve and reject are explicit command endpoints. The domain aggregate permits either transition only from `PendingReview`, making the review decision terminal even when a client retries or another client acts concurrently. CORS is restricted to the two loopback Vite origins used during local development.

## Catalog organization

Catalog is a first-class aggregate used to group related products. The API exposes catalog creation/listing and accepts an optional catalog filter on product listing. New UI-created products always carry the selected catalog ID; application validation prevents references to unknown catalogs. Existing pre-catalog products remain valid with a null catalog ID for migration compatibility.

Development startup applies pending migrations and inserts an idempotent, varied sample set. Production environments never receive sample data automatically.
