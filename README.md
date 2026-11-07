# Product Intelligence Hub

Product Intelligence Hub is a local-first product information management application. It organizes products into catalogs and uses a local Ollama model to generate German and English product descriptions from trusted product facts.

Administrators can create catalogs and products, generate and edit drafts, inspect model runs, approve or reject content, and select one approved version as the current published description. Viewer accounts see only published catalog content.

## Technology

- .NET 10 and ASP.NET Core
- Entity Framework Core with SQL Server
- React, TypeScript, Vite, shadcn/ui, and TanStack Table
- Ollama for local text generation and embeddings
- Qdrant for vector retrieval
- Docker Compose for the API, SQL Server, and Qdrant
- OpenTelemetry log export for telemetry-hub

## Requirements

Install:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [Node.js](https://nodejs.org/) 22.18 or newer
- [Ollama](https://ollama.com/download)
- Bash (Git Bash or WSL on Windows)

Pull the local AI models:

```bash
ollama pull qwen3:8b
ollama pull nomic-embed-text
```

## Install and run

Clone the repository and create the local environment file:

```bash
git clone <repository-url>
cd product-intelligence-hub
```

In Bash (including Git Bash or WSL on Windows):

```bash
cp .env.example .env
bash ./scripts/start-producthub.sh
```

The launcher:

1. Loads configuration from `.env`.
2. Builds and starts the API, SQL Server, and Qdrant with Docker Compose.
3. Installs frontend packages when needed.
4. Starts the React development server.

Open:

- Product Hub: [http://localhost:5173](http://localhost:5173)
- API: [http://localhost:5099](http://localhost:5099)
- OpenAPI document: [http://localhost:5099/openapi/v1.json](http://localhost:5099/openapi/v1.json)
- Qdrant: [http://localhost:6333](http://localhost:6333)

Application logs and frontend process information are written to `.run/`.

## Demo accounts

| Role | Email | Password |
| --- | --- | --- |
| Administrator | `admin@producthub.local` | `Admin123!` |
| Viewer | `viewer@producthub.local` | `Viewer123!` |

These accounts are for local development only.

## Stop the application

```bash
bash ./scripts/stop-producthub.sh
```

To stop and remove all Docker containers:

```bash
docker compose down
```

Add `--volumes` only when you intentionally want to remove local SQL Server and Qdrant data.

## Main workflow

1. Sign in as an administrator.
2. Create or select a catalog.
3. Add products and factual attributes.
4. Choose whether to include product facts, select No RAG, Naive RAG, Advanced RAG, or Graph RAG, and generate a German or English draft with Ollama.
5. Review or edit the generated content.
6. Approve or reject one or more drafts.
7. Mark one approved draft as the current published version.
8. Sign in as the viewer to see the published catalog.

The application records prompt version, model, token usage, latency, status, correlation ID, facts inclusion, retrieval mode, and source citations for every generation.

## Generation and RAG options

The generation dialog independently controls **Include product facts** and **Document retrieval**. Name, brand, and SKU are always supplied. Turning facts off removes structured attributes from retrieval queries and the generation prompt.

### Generation, from sources to draft

It helps to think of generation as four separate steps. **Structured product facts and uploaded documents are different inputs**: facts describe the product directly, while documents provide passages that may be useful context.

#### 1. Product identity and source facts

Product Hub always provides the product's SKU, name, and brand as identity. The product's structured attributes (for example, material or dimensions) are optional: **Include product facts** controls whether they are added to the retrieval query and generation context.

#### 2. Upload and index reference documents

When a document is uploaded, Product Hub extracts text from supported files, normalizes whitespace, then splits the text into ordered, overlapping chunks. By default, chunks are about 1,200 characters with 200 characters of overlap. The splitter prefers a nearby space when it can do so without making a chunk too short. It does not split by headings or understand document meaning; the order of the passages is retained.

SQL Server stores the document and authoritative chunk text. Ollama's embedding model (`nomic-embed-text` by default) converts each chunk into a vector, and Qdrant stores the vector with its chunk ID and product/document identifiers. A document is available as evidence only after indexing completes. Qdrant is a **meaning-based index**: it helps find likely passages, but does not store the authoritative text or generate the draft.

#### 3. Retrieve relevant passages (RAG)

For a RAG request, Product Hub forms a search query from product identity and, if enabled, structured attributes. Ollama converts this query into an embedding. Qdrant compares it with document-chunk vectors and returns matching chunk IDs. Product Hub fetches the actual text from SQL Server and checks that it belongs to the product and is from a completed, active document.

| Mode | How passages are selected |
| --- | --- |
| No RAG | Skips retrieval. No query embedding or Qdrant search is performed. |
| Naive RAG | One semantic search in Qdrant; use its best matching passages. |
| Advanced RAG | Up to three semantic searches in Qdrant, combined with independent SQL keyword candidates; BM25 and reciprocal rank fusion rank the results, then near-duplicates and excess context are removed. |
| Graph RAG | Uses Advanced RAG to find starting passages, then follows shared-concept and adjacent-passage links in a bounded graph built from SQL chunks. |

Top-K is the requested maximum number of passages, not an ordering setting; Product Hub ranks them automatically. Naive RAG keeps the semantic-search order, Advanced RAG ranks and diversifies its combined results, and Graph RAG returns semantic anchors before graph-linked passages. Fewer than Top-K may be returned if there are not enough eligible matches or the context limit is reached. The retrieved context is limited to 12,000 characters, with each excerpt capped at 2,400 characters. Graph RAG's graph covers at most 500 chunks per product and follows at most two hops. The graph is a heuristic over text, not a separate graph database or the full Microsoft GraphRAG pipeline. Advanced and Graph RAG reuse the same indexed vectors; they do not require reindexing.

#### 4. Generate and review

The generation model receives product identity, optional structured facts, and (for RAG modes) the selected document passages. Ollama generates the draft; Product Hub validates and saves it with its generation settings and source evidence for review. Uploaded documents are reference material, not proof that every statement in them is a verified product fact. Disabling structured facts does not remove information from uploaded documents.

Queued jobs and the synchronous `/drafts/generate` endpoint use the same generation service; queued jobs run it in the background. A queued No RAG job may still display a `Retrieving` stage even though it skips retrieval. Retrieval failures stop generation rather than silently falling back. Reusing an idempotency key may return an existing queued job or draft, or reject a request already in progress or previously failed; it does not necessarily start a new attempt.

API example for advanced RAG without structured facts:

```json
{
  "language": "en",
  "model": "qwen3:8b",
  "includeFacts": false,
  "retrievalMode": "Advanced",
  "topK": 5
}
```

Send this to `POST /api/products/{productId}/drafts/generate` with an administrator session and a fresh `Idempotency-Key` header. Existing requests with `useRag: true` select naive RAG when `retrievalMode` is omitted.

This includes migration `20261103120000_AddGenerationFactsSelection` and defaults to prompt `product-description-v4`. Development startup applies pending migrations; other deployments must apply them before restarting the API:

```bash
dotnet ef database update --project src/ProductHub.Infrastructure --startup-project src/ProductHub.Api
```

See [Generation and RAG](docs/architecture/generation-and-rag.md) for all eight combinations, ranking details, limits, audit fields, and rollout instructions.

## telemetry-hub integration

Product Hub exports structured API logs and distributed traces to [telemetry-hub](https://github.com/burakcitaci/telemetry-hub) using OpenTelemetry OTLP/HTTP. ASP.NET Core requests, outgoing HTTP calls, and SQL Server commands are traced. Generation logs include model latency and token counts. Product Hub does not currently export metrics.

1. Start telemetry-hub by following its [installation guide](https://github.com/burakcitaci/telemetry-hub#first-installation), and keep its backend port-forward running (`yarn ports:backend`).
2. Sign in to the telemetry-hub dashboard, open **Administration → Ingestion-Keys**, select a team, and generate a client key. Copy the key when it is displayed; its plaintext is shown only once.
3. Add the key to Product Hub's ignored local `.env` file:

```dotenv
OTEL_EXPORTER_OTLP_HEADERS=x-api-key=<key-generated-in-telemetry-hub>
```

The Compose configuration sends logs to `http://host.docker.internal:3001/api/ingest`, which reaches telemetry-hub's host port-forward from the API container. `localhost` inside the API container refers to that container, not your machine. If running the API directly on your host instead of in Compose, set `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:3001/api/ingest`.

The protocol defaults to OTLP/HTTP protobuf and the service name defaults to `product-hub-api`; set `OTEL_SERVICE_NAME` in `.env` only if you want a different service label.

Restart the API so it reads the new key:

```bash
docker compose up -d --force-recreate api
```

Generate API activity, then select the `product-hub-api` service and `Development` environment in telemetry-hub's **Logs** view. Never commit or share the ingestion key. The local HTTP endpoint is for development; use HTTPS and appropriate secure transport settings for non-local deployments.

## Build and test

Backend:

```bash
dotnet restore ProductHub.slnx
dotnet build ProductHub.slnx --no-restore
dotnet test tests/ProductHub.UnitTests/ProductHub.UnitTests.csproj --no-build
```

Frontend:

```bash
cd src/ProductHub.Web
npm ci
npm test
npm run build
```

Integration tests use an isolated SQL Server Testcontainer:

```bash
dotnet test tests/ProductHub.IntegrationTests/ProductHub.IntegrationTests.csproj
```

New generations use an explicit output schema and audit temperature, seed, and output-token limits. Apply migration 20261104100000_AddGenerationParameters before running the updated API; development startup applies pending migrations. See [generation documentation](docs/architecture/generation-and-rag.md#schema-constrained-generation).

New drafts now include expandable claim/evidence review with saved source snapshots. Sparse evidence permits zero to five highlights and explicit warnings. Flagged drafts require authenticated human acknowledgement before approval/publication. This is conservative review assistance, not automated fact verification. Apply migration 20261104110000_AddDraftClaimReview. See [claim review documentation](docs/architecture/generation-and-rag.md#claim-review-and-saved-evidence).

## Review queue and version comparison

Catalogs now offer server-backed filters for pending review, missing published language, missing source facts and stale published content. Search, language and pagination persist while navigating within a browser session. Counts cover the whole matching catalog, including products beyond the first 100.

Saved drafts can be compared with the published version or another saved version, with inline changes and generation settings. Unsaved edits show a warning and navigation guard; background generation keeps the current draft/editor visible. See [Review workspace](docs/ui-review-workspace.md) for definitions, API behavior, limits and verification.

## Durable AI generation

The UI queues generation in SQL Server and reconnects to progress when a product is reopened. Stages include Queued, Retrieving, Generating, Validating and Ready. Model audit includes failed and cancelled attempts even when no draft was created, with status/language filters, errors, settings and evidence; matched job/generation records appear once. Cancellation is available, existing content stays visible, and interrupted model calls are not automatically repeated. Apply migration `20261104120000_AddGenerationJobs` before starting the updated API. The synchronous `/drafts/generate` endpoint remains available.

Advanced and Graph RAG now retrieve independent SQL keyword candidates alongside vector results, including bounded English/German synonyms and exact model tokens. Source audits include document/chunk revisions, candidate counts and graph cache provenance. Named generation presets save common choices in the current browser.

See [Durable generation jobs](docs/architecture/generation-jobs.md) and [generation/retrieval details](docs/architecture/generation-and-rag.md). Recorded verification includes 32 integration tests, 18 desktop/mobile browser checks, 16 offline evaluator checks and 17 live API checks. The final unit-test binary was restricted by Windows application control. See [measured acceptance results](docs/evaluation-results-2026-11-04.md), including the model numeric error caught by review and the optional reranker decision.

Knowledge sources now support explicit supersession and extraction-version auditing. Apply migration `20261104130000_AddDocumentRevisions` along with the earlier pending migrations. Old evidence stays available for review while retrieval uses active sources. Failed vector writes persist Failed status rather than advertising ready documents.

Run browser acceptance checks with `npm run test:browser` from the web project (installed Edge on Windows; `npx playwright install --with-deps chromium` on Linux). CI runs the production-build desktop/mobile suite. Workspace links restore the selected product after reload, and lost generation submissions retain an idempotency key for safe retries.

## Archived AI evaluation results

The optional evaluation tools have been removed to keep clone and runtime requirements small. Historical observations and their limitations are documented in [acceptance results](docs/evaluation-results-2026-11-04.md).

## Scripts

| Script | Purpose |
| --- | --- |
| `start-producthub.sh` | Start the local application |
| `stop-producthub.sh` | Stop API and frontend processes |
| `invoke-ollama-smoke-test.sh` | Generate a draft through the API |

All scripts are in `scripts/`. Run them from Bash.

## Architecture

Product Hub is a modular monolith with Domain, Application, Infrastructure, and API projects. SQL Server stores authoritative product, draft, generation, and document data. Qdrant stores the rebuildable vector projection used by retrieval.

More detail is available in:

- [Architecture overview](docs/architecture/overview.md)
- [Data model](docs/architecture/data-model.md)
- [Generation and RAG](docs/architecture/generation-and-rag.md)
- [UI and AI implementation roadmap](docs/ui-ai-roadmap.md)
- [Acceptance results](docs/evaluation-results-2026-11-04.md)
- [Modular-monolith ADR](docs/adr/0001-modular-monolith.md)
- [Local Ollama ADR](docs/adr/0002-local-ollama.md)
