# ADR 0005: Local React review workspace

**Status:** Accepted for M5.

## Decision

Use a Vite-powered React and TypeScript single-page client as a separate local process. It calls the ASP.NET Core API on loopback and keeps all generation and review rules server-side.

Draft approval and rejection are terminal domain transitions exposed as command endpoints. The client displays immutable product facts beside generated content and exposes provider, model, prompt, latency, token, and trace metadata for review.

## Consequences

The UI can evolve independently without weakening Clean Architecture boundaries. Local development requires Node.js in addition to .NET, Docker, and Ollama. M5 intentionally has no authentication and is suitable only for a trusted local machine; production deployment must add identity, authorization, and CSRF-aware hosting decisions.
