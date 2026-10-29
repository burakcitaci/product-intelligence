# ADR 0004: Bounded agent workflows with human approval

## Status
Accepted for M4.

## Decision
Use Ollama to produce a structured plan containing calls to an application-owned allow-list: `product.lookup`, `product.validate`, and `product.propose_update`. The application validates and executes every call; the model never invokes infrastructure directly.

Read-only tools execute automatically. A proposed mutation persists as `AwaitingApproval` and is applied only through the explicit approval endpoint. Rejection is terminal.

## Limits

Plans are capped at five steps by default, arguments at 500 characters per value, objectives at 1,000 characters, and execution at 120 seconds. Unknown tools, malformed arguments, excess steps, and timeouts fail closed and are recorded on the workflow.

## Consequences

The tool surface is deliberately small and auditable. Adding a tool requires an application implementation and allow-list change. M4 has no authentication, so approval endpoints remain suitable only for trusted localhost development.
