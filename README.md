# Product Intelligence Hub

Production-style, local-first product information platform implementing **M1–M5**, from product catalog through a human review UI.

## Implemented

- .NET 10 modular monolith with Clean Architecture boundaries
- Product aggregate with extensible JSON attributes
- User-created catalogs that organize related products
- SQL Server persistence, migration, unique SKU constraint, and row-version concurrency
- `POST /api/products`, `GET /api/products`, and `GET /api/products/{id}`
- Typed DTOs, validation, pagination, OpenAPI, and Problem Details
- Unit tests and SQL Server Testcontainers API integration tests
- Local German/English content generation through Ollama and `Microsoft.Extensions.AI`
- Versioned, embedded prompts with untrusted product-data isolation
- Review-only content drafts, generation audit metadata, failure recording, and idempotency
- Text, Markdown, CSV, JSON, XML, and PDF document ingestion
- Ollama `nomic-embed-text` embeddings, persistent Qdrant vector search, and source citations
- Baseline RAG evaluation fixtures under `tests/evaluation`
- Bounded local agent workflows with allow-listed product tools and human approval gates
- Responsive React review workspace with product search, side-by-side source facts and AI copy, generation audit data, and terminal approve/reject decisions

The included authentication is intended for local demonstration; production identity integration remains a future milestone.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker Desktop with Linux containers for SQL Server and integration tests
- [Ollama](https://ollama.com/download) for manual AI generation
- Node.js and npm
- PowerShell 7 on Windows, or Bash on macOS/Linux

## Start locally

Create the local environment file first:

```powershell
Copy-Item .env.example .env
```

On Windows, use the PowerShell launcher:

```powershell
.\scripts\Start-ProductHub.ps1
```

On macOS/Linux, use the Bash launcher:

```bash
cp .env.example .env
./scripts/start-producthub.sh
```

The launcher loads `.env`, builds and starts the Docker services, installs frontend dependencies when needed, and starts the React development server. Logs are written to `.run`. Stop the application with:

```powershell
.\scripts\Stop-ProductHub.ps1
```

```bash
./scripts/stop-producthub.sh
```

For manual startup:

```powershell
Copy-Item .env.example .env
# Edit .env and replace the sample password.
docker compose up -d --wait
$env:ConnectionStrings__ProductHub = 'Server=127.0.0.1,1433;Database=ProductHub;User Id=sa;Password=<same-password>;TrustServerCertificate=True;Encrypt=True'
dotnet tool restore
dotnet ef database update --project src/ProductHub.Infrastructure --startup-project src/ProductHub.Api
dotnet run --project src/ProductHub.Api --launch-profile http
```

The `http` launch profile binds to localhost only. OpenAPI JSON is available in Development at `/openapi/v1.json`.

Create and retrieve a product:

```powershell
$product = Invoke-RestMethod -Method Post -Uri http://localhost:5099/api/products -ContentType application/json -Body (@{
  sku = 'BOTTLE-750'
  name = 'Trail Bottle'
  brand = 'Northwind'
  attributes = @(@{ name = 'Volume'; value = '750'; unit = 'ml' })
} | ConvertTo-Json -Depth 4)

Invoke-RestMethod -Uri "http://localhost:5099/api/products/$($product.id)"
Invoke-RestMethod -Uri 'http://localhost:5099/api/products?page=1&pageSize=20'
```

## Local AI generation

Install/start Ollama on Windows, then pull the default model:

```powershell
ollama pull qwen3:8b
ollama pull nomic-embed-text
ollama list
ollama ps
```

`ollama ps` shows the active processor split after a request. A GPU/CPU split containing GPU indicates acceleration; `100% CPU` indicates the supported CPU fallback. On AMD Windows systems, acceleration depends on the installed Ollama release and compatible Radeon drivers—verify rather than assume it.

With SQL Server, Ollama, and the API running, generate a draft:

```powershell
$headers = @{ 'Idempotency-Key' = "generate-$([guid]::NewGuid().ToString('N'))" }
$draft = Invoke-RestMethod -Method Post -Uri "http://localhost:5099/api/products/$($product.id)/drafts/generate" `
  -Headers $headers -ContentType application/json -Body '{"language":"de"}'
Invoke-RestMethod -Uri "http://localhost:5099/api/drafts/$($draft.id)"
Invoke-RestMethod -Uri "http://localhost:5099/api/products/$($product.id)/drafts"
```

The response is always a `PendingReview` draft. Reusing a successful idempotency key returns the same draft; in-progress or failed keys return `409`. Use a new key for an explicit retry after failure.

The optional manual smoke-test helper is:

```powershell
./scripts/Invoke-OllamaSmokeTest.ps1 -ProductId $product.id -Language de
```

The Bash equivalent accepts product ID and language as positional arguments:

```bash
./scripts/invoke-ollama-smoke-test.sh "$PRODUCT_ID" de
```

Override settings with `Ai__Model`, `Ai__Endpoint`, `Ai__TimeoutSeconds`, or `Ai__PromptVersion`. No cloud provider fallback exists.

Check the actual port printed by `dotnet run`; the launch profile is authoritative if it changes from `5099`.

## Review workspace

With the API running on port `5099`, start the local React client in a second terminal:

```powershell
Set-Location src/ProductHub.Web
npm install
npm run dev
```

Open `http://localhost:5173`. The workspace creates and browses catalogs, adds products with factual attributes, generates German or English drafts, lets users edit pending AI copy, compares supplied facts with generated copy, shows model audit metadata, and supports approval, rejection, and selecting one approved version as current. The browser talks only to the local API; no hosted frontend or cloud font dependency is required.

In Development, startup seeds three sample catalogs—Outdoor Essentials, Home Coffee, and Smart Workspace—with three distinct products each. Seeding is idempotent, so restarting the API does not duplicate them.

## Build and test

```powershell
dotnet restore ProductHub.slnx
dotnet build ProductHub.slnx --no-restore
dotnet test tests/ProductHub.UnitTests --no-build
dotnet test tests/ProductHub.IntegrationTests --no-build
```

Integration tests create an isolated SQL Server container, apply migrations, and never call external AI services.

## Stop infrastructure

```powershell
docker compose down
# To intentionally remove local database data as well:
docker compose down --volumes
```

## Configuration and security

`ConnectionStrings__ProductHub` overrides the development placeholder in `appsettings.json`. Keep real credentials in `.env` or user secrets; `.env` is ignored. M1 has no authentication and is for trusted local development only. Docker and the launch profile expose services on loopback.

## Troubleshooting

- **Login failed:** confirm the API connection-string password matches `MSSQL_SA_PASSWORD` in `.env`.
- **Container unhealthy:** SQL Server requires a strong password and enough Docker memory. Inspect with `docker compose logs sqlserver`.
- **Port 1433 occupied:** change the host side of the Compose port and the API connection string together.
- **Integration tests skipped/fail before execution:** ensure Docker Desktop is running and Linux containers are enabled.
- **Migration command unavailable:** run `dotnet tool restore` from the repository root.
- **Ollama connection failure:** verify `Invoke-RestMethod http://localhost:11434/api/tags`, then confirm `Ai__Endpoint` from the API process can reach the Windows host.
- **Model unavailable:** run `ollama list`, pull the configured model, and ensure `Ai__Model` matches exactly.
- **Generation timeout:** increase `Ai__TimeoutSeconds` up to 600, or use a smaller model/CPU-friendly quantization.
- **Invalid JSON:** inspect the failed `AiGenerations.ErrorCode`; the application records `invalid-json` or `invalid-output` and never fabricates a draft.
- **Qdrant unavailable:** verify `docker compose ps` and open `http://localhost:6333/healthz`.
- **Embedding unavailable:** run `ollama list`, pull `nomic-embed-text`, and verify `http://localhost:11434/api/tags`.

Ollama is never required for automated builds or tests. Frontend checks are `npm run typecheck` and `npm run build` from `src/ProductHub.Web`.

## Local document retrieval

Compose starts both SQL Server and Qdrant. After applying migrations and starting the API, upload a supported document and search it:

```powershell
$document = Invoke-RestMethod -Method Post -Uri http://localhost:5099/api/documents -Form @{
  file = Get-Item .\tests\evaluation\sample-bottle-manual.md
}

$results = Invoke-RestMethod -Method Post -Uri http://localhost:5099/api/knowledge/search `
  -ContentType application/json -Body '{"query":"What is the bottle volume?","limit":5}'
$results | Format-Table score,citation,text -Wrap
```

Uploads are limited to 10 MiB and deduplicated by SHA-256. Chunk text and source metadata live in SQL Server; Qdrant stores a rebuildable vector projection. Override defaults with `Rag__EmbeddingModel`, `Rag__QdrantEndpoint`, `Rag__VectorSize`, `Rag__ChunkSize`, or `Rag__ChunkOverlap`. The configured vector size must match the embedding model.

## Agent workflows

Start a bounded workflow for an existing product:

```powershell
$workflow = Invoke-RestMethod -Method Post -Uri http://localhost:5099/api/agent/workflows `
  -ContentType application/json -Body (@{
    productId = $product.id
    objective = 'Inspect this product and validate its data quality. Do not modify it.'
  } | ConvertTo-Json)
```

Read-only workflows complete automatically. A workflow that proposes a product change returns `AwaitingApproval`. Review its `proposal`, then explicitly approve or reject it:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5099/api/agent/workflows/$($workflow.id)/approve"
# or
Invoke-RestMethod -Method Post -Uri "http://localhost:5099/api/agent/workflows/$($workflow.id)/reject"
```

Allowed tools are `product.lookup`, `product.validate`, and `product.propose_update`. Defaults are five steps, 500 characters per argument, and a 120-second execution limit. Configure them with `Agent__MaxSteps`, `Agent__MaxArgumentLength`, and `Agent__TimeoutSeconds`.

## Design notes

See [architecture overview](docs/architecture/overview.md), [data model](docs/architecture/data-model.md), and the [modular-monolith ADR](docs/adr/0001-modular-monolith.md).

## OpenTelemetry logs

The API can export its existing `ILogger` records to the telemetry-hub OTLP gateway. Add these values to the local `.env` file:

```dotenv
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:3001/api/ingest
OTEL_EXPORTER_OTLP_HEADERS=x-api-key=<provisioned-client-key>
OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
OTEL_SERVICE_NAME=product-hub-api
```

The exporter posts OTLP/HTTP protobuf log batches to `/api/ingest/v1/logs`. If the endpoint or headers are absent, remote log export remains disabled and the API continues using its normal local logging. Do not commit the client key.

When a local telemetry-hub Kubernetes environment is available, the helper scripts can provision and configure a scoped ingestion key:

```powershell
.\scripts\Configure-TelemetryHub.ps1
```

```bash
./scripts/configure-telemetry-hub.sh
```

The Bash version requires `kubectl`, `jq`, `openssl`, `base64`, and Python 3.

