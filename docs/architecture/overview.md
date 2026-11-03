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

## Generation sources and advanced RAG

Generation independently selects structured facts and retrieval mode (None, Naive, Advanced). Name, brand, and SKU always identify the product; excluded attributes are omitted from retrieval queries and generation. Explicit modes take precedence over the legacy `useRag` flag.

Advanced retrieval expands supplied query clauses, retrieves a bounded candidate union, applies BM25 candidate reranking and reciprocal rank fusion, removes near duplicates, and enforces an excerpt budget. SQL validates completed status and product ownership for both paths. Failed or empty retrieval aborts generation.

The generation audit persists facts inclusion and retrieval mode. Prompt v3 handles optional facts while keeping specifications authoritative and reference guidance advisory. See [Generation and RAG](generation-and-rag.md) for the algorithm, API, and rollout.

## M4 agent workflow

An agent request creates a durable `Running` workflow before model inference. Ollama returns a structured sequence of tool calls. Application code validates tool names, step count, arguments, and execution time before dispatching read-only product lookup or validation tools.

`product.propose_update` stores a proposal and transitions the workflow to `AwaitingApproval`; it does not mutate the product. Separate approve and reject endpoints make the human decision explicit and auditable. Approval reloads the tracked product, applies the domain update, and completes the workflow in one SQL transaction.

## M5 review workspace

The Vite/React client is a separate local process and consumes only HTTP application contracts. It never reads persistence entities or calls Ollama directly. Product selection loads saved drafts and generation metadata; draft creation remains an API operation protected by an idempotency key.

Approve and reject are explicit command endpoints. Approval requires a pending draft; rejection accepts pending or approved drafts and clears the current published flag. Repeated rejection and edits to reviewed drafts return conflict. CORS is restricted to the two loopback Vite origins used during local development.

## Catalog organization

Catalog is a first-class aggregate used to group related products. The API exposes catalog creation/listing and accepts an optional catalog filter on product listing. New UI-created products always carry the selected catalog ID; application validation prevents references to unknown catalogs. Existing pre-catalog products remain valid with a null catalog ID for migration compatibility.

Development startup applies pending migrations and inserts an idempotent, varied sample set. Production environments never receive sample data automatically.
