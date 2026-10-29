# ADR 0001: Modular monolith

- Status: Accepted
- Date: 2026-10-27

## Decision

Use a single deployable API split into Domain, Application, Infrastructure, and API projects. Business rules remain independent of transport and persistence. Avoid MediatR, generic repositories, and unit-of-work wrappers until demonstrated complexity justifies them.

## Consequences

Development, debugging, transactions, and deployment remain simple. Module boundaries are enforced by project references rather than network calls. Future extraction remains possible, but is not a goal.
