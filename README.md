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

Retrieval-augmented generation (RAG) adds relevant passages from uploaded reference documents to the draft prompt. **Include product facts** controls the structured attributes, while **Document retrieval** controls the reference passages. You can use either input on its own or combine them. Product name, brand, and SKU are always included.

### 1. Start with the QuietBurr Coffee Grinder

Sign in as an administrator and open [QuietBurr Coffee Grinder in German](http://localhost:5173/?language=de&catalog=60da283a-0a6f-4b76-ba20-70bc776044f9&product=9613c7db-b46f-477e-8641-66a0d2527234). Catalog and product IDs are assigned locally. On a fresh setup, choose **Home Coffee → QuietBurr Coffee Grinder** and select German.

| Input | QuietBurr example |
| --- | --- |
| Identity | QuietBurr Coffee Grinder, Morning Craft, `COF-GRN-01` |
| Structured facts | 40 grind settings, 250 g hopper capacity, 150 W power |
| Reference document | [Grinder.pdf](docs/examples/Grinder.pdf), a general coffee grinder guide covering operation, grind size, cleaning, and troubleshooting |

The structured facts supply the specs for this SKU. The guide can supply advice such as unplugging a grinder before cleaning. Its mention of an automatic timer is generic guidance and does not establish that QuietBurr has a timer.

### 2. Turn the reference guide into searchable chunks

In **New draft → Product knowledge**, select `docs/examples/Grinder.pdf` from your clone and click **Upload and index**. The example citations use the bundled filename `Grinder.pdf`.

1. **Product Hub** extracts the text, normalizes whitespace, and takes windows of up to 1,200 characters by default. Windows start 1,000 characters apart, giving up to 200 characters of overlap. A window may end a little early at a space, and it can span multiple sections of the guide.
2. **Ollama** runs `nomic-embed-text` to turn each chunk into a 768-dimensional vector with the default settings.
3. **Qdrant** indexes each vector in `product-knowledge` under its chunk ID, with product ID, document ID, and chunk ordinal as metadata. **SQL Server** holds the chunk text and document status. The document becomes eligible for retrieval once indexing completes.

The indexed guide has four chunks. For example, both `chunk-1` and `chunk-2` contain the instruction to unplug the grinder and empty its containers. That overlap carries the start of the cleaning instructions into the chunk that continues them.

| Citation | Characters | Content covered |
| --- | ---: | --- |
| `Grinder.pdf#chunk-0` | 1,197 | Safety, first use, and basic operation |
| `Grinder.pdf#chunk-1` | 1,195 | Operation, grind-size guidance, and the start of cleaning |
| `Grinder.pdf#chunk-2` | 1,198 | Cleaning, troubleshooting, and the start of storage |
| `Grinder.pdf#chunk-3` | 365 | End of troubleshooting, storage, and disposal |

### 3. Compare the four retrieval modes

With **Include product facts** on, Product Hub builds this search query from the saved product record. The German selection controls the draft language. Search uses the names and labels as stored.

```text
COF-GRN-01 | QuietBurr Coffee Grinder | Morning Craft | Grind settings: 40  | Hopper capacity: 250 g | Power: 150 W
```

For each search query, Ollama's embedding model produces a query vector. Qdrant filters to the selected product and returns chunk IDs ranked by cosine similarity to that vector. Product Hub loads the passages from SQL Server and checks that they belong to the product and come from completed, active documents. All three RAG modes reuse the same index.

| Mode | How Product Hub chooses passages | Result for this guide with Top-K 3 |
| --- | --- | --- |
| **No RAG** | Skip document retrieval. Ollama runs only the draft model. | Identity and the three structured facts go into the prompt. |
| **Naive RAG** | Embed one query, search Qdrant once, and keep eligible hits in cosine-similarity order. | `chunk-1` → `chunk-2` → `chunk-0`. Grind-size guidance ranks first. |
| **Advanced RAG** | Search Qdrant with up to three query variants and independently find SQL keyword matches. Use BM25 to rank the combined candidates by keyword relevance, then use reciprocal rank fusion to merge the keyword and vector rankings. Favor passages that add new context. | `chunk-2` → `chunk-0` → `chunk-1`. Cleaning ranks first. |
| **Graph RAG** | Start with Advanced RAG results as anchors. Follow shared-term links or links between consecutive chunks in the same document. | `chunk-2` → `chunk-0` → `chunk-1`. The first two are anchors. The last is reached from `chunk-2` in one hop. |

The RAG rows show one retrieval-only run against the indexed guide. Graph and Advanced returned the same passages in this run. Graph recorded the path `Grinder.pdf#chunk-2 → shared-concept:2.empty → Grinder.pdf#chunk-1`, shown here with the bundled filename. The `2.empty` label comes from the numbered cleaning instruction that appears in both chunks. Graph links are based on text and chunk order. The graph uses up to 500 active chunks for the product and explores up to two hops.

**Top-K** is the maximum number of excerpts. Eligibility checks can reduce the count. Advanced and Graph also remove near-duplicates and cap context at 2,400 characters per excerpt and 12,000 characters across excerpts. Turning **Include product facts** off removes the structured attributes from the query and prompt. Any information already in a retrieved passage can still appear in the reference context.

### 4. Generate and inspect the German draft

1. In **New draft**, select `qwen3:8b`, keep **Include product facts** on, and choose **Graph RAG** with **Top-K** set to `3`.
2. Click **Generate draft**. Ollama now runs the draft model with the German language setting, product identity, structured facts, and retrieved excerpts. Product Hub validates and saves the response as a draft. A failed or empty RAG search stops generation before the draft model is called.
3. Open **Model audit → Attempt details** to see citations, retrieval mode, and graph paths. In the draft's claim review, check that a spec such as `250 g Fassungsvermögen` is backed by the structured product record and that any cleaning advice is grounded in the saved guide excerpt. Keep the language, model, and facts setting fixed when comparing the four retrieval modes.

The UI runs generation as a background job. To run the same generation synchronously, send this body to `POST /api/products/{productId}/drafts/generate` with an administrator session and a fresh `Idempotency-Key` header.

```json
{
  "language": "de",
  "model": "qwen3:8b",
  "includeFacts": true,
  "retrievalMode": "Graph",
  "topK": 3
}
```

To compare passages without creating drafts, send `query` (the text above), `productId`, `limit: 3`, and `mode` (`Naive`, `Advanced`, or `Graph`) to `POST /api/knowledge/search`. Existing generation requests with `useRag: true` select Naive when `retrievalMode` is omitted.

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
