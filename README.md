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
- [Node.js](https://nodejs.org/) 22 or newer
- [Ollama](https://ollama.com/download)
- PowerShell 7 on Windows, or Bash on macOS/Linux

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
cp .env.example .env
```

On Windows PowerShell:

```powershell
Copy-Item .env.example .env
.\scripts\Start-ProductHub.ps1
```

On macOS or Linux:

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

Windows:

```powershell
.\scripts\Stop-ProductHub.ps1
```

macOS/Linux:

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

| Mode | Behavior |
| --- | --- |
| No RAG | Generate from identity and optional structured facts, without documents |
| Naive RAG | One semantic vector search over the selected product's uploaded knowledge |
| Advanced RAG | Up to three searches, wider candidate retrieval, BM25 candidate reranking, reciprocal rank fusion, near-duplicate removal, and context budgeting |
| Graph RAG | Advanced semantic anchors plus up to two hops through shared concepts and adjacent passages in a product-scoped document graph |

All three RAG modes require completed, indexed product documents and accept Top-K from 1 to 20 (default 5). Advanced RAG returns up to Top-K excerpts within a 12,000-character retrieved-context budget. It reuses Ollama embeddings and Qdrant; no extra service or reindexing is needed. Retrieval failure stops generation instead of silently falling back.

Graph RAG rebuilds a local graph from up to 500 completed chunks per product. It can bring in related text absent from vector results, keeps the same excerpt/context limits, and records paths in the model audit. Select **Graph RAG** in the dialog or send `"retrievalMode": "Graph"` in the generation request. No extra migration, graph database, or reindexing is required for this mode. This is a lexical chunk/concept graph rather than the full Microsoft GraphRAG indexing and community-summary pipeline.

Without facts and without RAG, generation is limited to product identity. Uploaded guidance remains advisory and must not become invented specifications. Review is required before publishing.

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

This includes migration `20261103120000_AddGenerationFactsSelection` and defaults to prompt `product-description-v3`. Development startup applies pending migrations; other deployments must apply them before restarting the API:

```bash
dotnet ef database update --project src/ProductHub.Infrastructure --startup-project src/ProductHub.Api
```

See [Generation and RAG](docs/architecture/generation-and-rag.md) for all eight combinations, ranking details, limits, audit fields, and rollout instructions.

## telemetry-hub integration

Product Hub can send its structured ASP.NET Core logs to telemetry-hub through OpenTelemetry OTLP/HTTP. This makes API activity and Ollama generation information available for centralized log search, dashboards, and operational monitoring. Generation logs include model latency and token counts.

Add a telemetry-hub ingestion endpoint and key to `.env`:

```dotenv
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:3001/api/ingest
OTEL_EXPORTER_OTLP_HEADERS=x-api-key=<provisioned-client-key>
OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
OTEL_SERVICE_NAME=product-hub-api
```

When telemetry-hub is running in the expected local Kubernetes environment, the helper can provision a scoped ingestion key and update Product Hub automatically.

Windows:

```powershell
.\scripts\Configure-TelemetryHub.ps1
```

macOS/Linux:

```bash
bash ./scripts/configure-telemetry-hub.sh
```

The Bash helper requires `kubectl`, `jq`, `openssl`, `base64`, and Python 3. Real ingestion keys are written only to the ignored local `.env` file.

Product Hub currently exports logs. The same OpenTelemetry foundation can later be extended with application metrics and distributed traces for richer telemetry-hub monitors.

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
npm run build
```

Integration tests use an isolated SQL Server Testcontainer:

```bash
dotnet test tests/ProductHub.IntegrationTests/ProductHub.IntegrationTests.csproj
```

## Scripts

| PowerShell | Bash | Purpose |
| --- | --- | --- |
| `Start-ProductHub.ps1` | `start-producthub.sh` | Start the local application |
| `Stop-ProductHub.ps1` | `stop-producthub.sh` | Stop API and frontend processes |
| `Configure-TelemetryHub.ps1` | `configure-telemetry-hub.sh` | Configure telemetry-hub log ingestion |
| `Invoke-OllamaSmokeTest.ps1` | `invoke-ollama-smoke-test.sh` | Generate a draft through the API |

All scripts are in the `scripts/` directory.

## Architecture

Product Hub is a modular monolith with Domain, Application, Infrastructure, and API projects. SQL Server stores authoritative product, draft, generation, and document data. Qdrant stores the rebuildable vector projection used by retrieval.

More detail is available in:

- [Architecture overview](docs/architecture/overview.md)
- [Data model](docs/architecture/data-model.md)
- [Generation and RAG](docs/architecture/generation-and-rag.md)
- [UI and AI implementation roadmap](docs/ui-ai-roadmap.md)
- [Modular-monolith ADR](docs/adr/0001-modular-monolith.md)
- [Local Ollama ADR](docs/adr/0002-local-ollama.md)
