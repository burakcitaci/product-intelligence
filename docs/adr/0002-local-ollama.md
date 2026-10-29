# ADR 0002: Local Ollama for future AI work

- Status: Accepted and implemented in M2
- Date: 2026-10-27

## Decision

Use Ollama on the Windows host behind the application-owned `IContentGenerator` abstraction and the provider-neutral `Microsoft.Extensions.AI.IChatClient`. The initial model is configurable and defaults to `qwen3:8b`. OllamaSharp is confined to Infrastructure.

## Consequences

No cloud credentials or usage costs are required. Hardware acceleration varies by Ollama and driver support; CPU inference remains a supported, slower fallback. Automated tests replace `IContentGenerator` and do not require Ollama or model weights.
