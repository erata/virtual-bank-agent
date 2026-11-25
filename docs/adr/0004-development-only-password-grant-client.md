# ADR-0004: Development-only password grant client for automated tests

- Status: Accepted
- Date: 2026-11-21

## Context
Bruno collections and integration tests need user tokens without a browser. The browser client
`virtual-bank-ui` uses Authorization Code + PKCE and must not allow the resource owner password grant,
which OAuth 2.1 removes.

## Decision
A separate public client `virtual-bank-cli` enables direct access grants only for local automation.
It carries the same protocol mappers as the UI client so tokens are equivalent. Service accounts use
client credentials.

## Consequences
- The UI client stays on the recommended flow.
- `virtual-bank-cli` and all `*-dev-only` secrets must be removed or replaced in any shared environment;
  this is stated in the README and threat model.
