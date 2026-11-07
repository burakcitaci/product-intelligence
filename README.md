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

### How generation uses RAG

Before a document can ground a draft, Product Hub extracts its text, splits it into overlapping chunks, and asks the configured local Ollama embedding model (`nomic-embed-text` by default) to create a vector for each chunk. SQL Server stores the document, chunk text, and ingestion status. Qdrant stores the chunk vectors with product/document identifiers so semantic searches can find likely matches. A document becomes eligible for retrieval only after indexing completes.

When generation is queued, the worker builds its retrieval query from product identity and, if enabled, structured product facts. The selected mode determines how uploaded evidence is found:

| Mode | Behavior |
| --- | --- |
| No RAG | Generate from identity and optional structured facts, without documents |
| Naive RAG | One semantic vector search over the selected product's uploaded knowledge |
| Advanced RAG | Up to three semantic searches plus independent SQL keyword candidates; BM25 and reciprocal rank fusion rank results, followed by near-duplicate removal and context budgeting |
| Graph RAG | Advanced RAG's semantic and keyword results seed a bounded, product-scoped graph of shared concepts and neighboring passages; retrieval follows up to two hops |

All three RAG modes require completed, indexed product documents and accept Top-K from 1 to 20 (default 5). Advanced RAG returns up to Top-K excerpts within a 12,000-character retrieved-context budget. It reuses Ollama embeddings and Qdrant; no extra service or reindexing is needed. Retrieval failure stops generation instead of silently falling back.

### Qdrant's role, in plain language

Think of Qdrant as a **meaning-based index for finding useful passages**:

1. On upload, Ollama turns each document chunk into a list of numbers (an embedding). Qdrant stores that list with the chunk's ID.
2. During a RAG generation, Ollama turns the product details into another list of numbers. Qdrant compares it with the stored lists and returns IDs of chunks whose meaning is similar.
3. Product Hub uses those IDs to fetch the actual text from SQL Server, checks that the source is still valid, and gives the selected passages to the generation model as reference.

For example, searching for a product described as “keeps you dry in heavy rain” may find a passage that says “waterproof shell,” even if the wording differs. Qdrant finds likely passages; it does not understand or write the answer. It stores vectors and IDs, not the authoritative document text. SQL Server owns the text and decides whether a document is eligible evidence.

Qdrant is used only when a RAG mode is selected. **No RAG does not call Qdrant.** Naive RAG uses its semantic matches directly. Advanced RAG also searches SQL for matching words and combines those results with Qdrant's. Graph RAG uses Qdrant to find starting passages, then follows related passages in a separate graph built from SQL data. Qdrant does not do keyword search, generate content, or store that graph. Its vectors can be rebuilt from the SQL-backed documents.

Advanced RAG adds independent lexical candidates from SQL, so it can find exact terms that vector search missed. Graph RAG builds and caches its bounded graph from current SQL chunks; Qdrant helps find the initial semantic anchors, while graph traversal may add related passages that were not direct vector hits. The graph is limited to 500 chunks per product and two traversal hops. It is a heuristic concept/adjacency graph, not the full Microsoft GraphRAG pipeline. Both advanced modes use the same chunk embeddings and Qdrant collection as Naive RAG; no extra reindexing or graph database is needed.

After retrieval, the worker sends the product context and selected excerpts to Ollama, validates the generated draft, and saves the draft and evidence for review. The job progresses through Queued, Retrieving, Generating, Validating, and a terminal status. Retrieval failures stop generation instead of silently falling back. Disabling facts excludes structured attributes, but does not remove information already present in an uploaded document.

Every new generation attempt follows this core pipeline, but not every attempt performs document retrieval: No RAG skips retrieval and does not call Qdrant or create query embeddings. Document extraction, chunking, and embedding happen when knowledge is uploaded, not on each generation. Queued generation and the synchronous `/drafts/generate` endpoint both use the same generation service; queued jobs run it in the background. The queued job can still report a `Retrieving` stage for No RAG even though it retrieves no documents. Reusing an idempotency key does not necessarily start a new attempt: a queued request can return its existing job, while synchronous generation can return an existing draft or reject a key already in progress or used by a failed generation.

Select **Graph RAG** in the dialog or send `"retrievalMode": "Graph"` in the generation request. Without facts and without RAG, generation is limited to product identity. Uploaded guidance remains advisory and must not become invented specifications. Review is required before publishing.

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
