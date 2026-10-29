# ADR 0003: Local Qdrant for vector search

## Status
Accepted for M3.

## Decision
Run Qdrant in Docker Compose and keep Ollama on the Windows host. Application interfaces isolate embedding generation and vector storage; Infrastructure calls the local Ollama and Qdrant HTTP APIs.

## Rationale
Qdrant provides persistent cosine-similarity search without cloud infrastructure. SQL Server remains authoritative for documents and chunk text, while Qdrant stores replaceable vector projections keyed by SQL chunk IDs.

## Consequences
An ingestion can leave an orphan Qdrant point if vector upsert succeeds and the following SQL save fails. Retrieval joins hits back to SQL and ignores missing chunks. A future reconciliation worker can remove orphan points when operational need justifies it.
