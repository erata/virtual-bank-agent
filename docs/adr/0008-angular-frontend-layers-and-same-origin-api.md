> **Current topology note:** the Assistant runs inside the same VirtualBank API (ADR-0011); older references below to a future standalone Agent service are historical design discussion.

# ADR-0008: Angular frontend — layered SPA, PKCE, same-origin API proxy

- Status: Accepted
- Date: 2026-11-22
- Related: ADR-0004 (development password-grant client), ADR-0006 (backend architecture)

## Context

M1 requires a minimal browser UI: Keycloak login, identity summary and the customer and credit features, so the
authorization model can be demonstrated with real people instead of curl. The UI must not become a second place
where access is decided, and it must stay testable without a browser for most of its logic.

## Decision

**Angular 22 SPA in `src/Frontend/virtual-bank-ui`, layered like the backend (ports and adapters):**

| Layer | Folder | Contains | May import |
| --- | --- | --- | --- |
| Domain | `app/domain` | UI models (`Identity`, `CustomerDetail`, `CreditApplication`), `Result`/`Problem`, input rules mirrored from the backend (id formats, note length) | nothing |
| Application | `app/application` | Ports `AuthSession`, `CustomerGateway`, `CreditGateway`; use cases `CustomerDirectory`, `CreditWorkspace` | domain, `@angular/core` |
| Infrastructure | `app/infrastructure` | Adapters: OIDC session (angular-auth-oidc-client), HTTP gateways, problem mapping, bearer interceptor, runtime config | application, domain, frameworks |
| Presentation | `app/presentation` | Shell, pages, `ProblemView`, notice texts, route guard | application, domain, Angular UI packages |
| Composition root | `app/app.config.ts` | Binds ports to adapters | everything |

`architecture/layer-rules.test.mjs` enforces the table (plus: only the auth adapter touches browser storage, only
the composition root wires adapters, no approval action in the UI). Mutation-checked.

**Authentication:** Authorization Code + PKCE (S256) with the public client `virtual-bank-ui`; renewal by refresh
token (no hidden iframes, no third-party cookies); logout revokes tokens and ends the Keycloak session.

**Same origin for app and API:** nginx serves the build and reverse-proxies `/api/` to the API. Consequences: no CORS configuration on the API, and the interceptor sends the token **only** to
relative `/api/` URLs — never to Keycloak, never to another origin (unit- and browser-tested).

**Runtime configuration:** `/config.json` (Keycloak URL, realm, client id) is rendered by nginx from environment
variables, validated at startup (`https` required except for localhost), so one image runs in any environment.

**Browser hardening:** strict CSP (`script-src 'self'`, `connect-src 'self' <keycloak>`, `frame-ancestors 'none'`),
no inline scripts (Angular's inline critical-CSS loader is disabled for this reason), `nosniff`, `no-referrer`,
`X-Frame-Options: DENY`; API responses `no-store`; unknown `/api/*` paths are answered by the API (404), never by the SPA.
A browser test fails on any CSP violation.

**Authorization stays server-side.** The identity card shows token claims for transparency only. Guards and
hidden buttons are convenience; forbidden actions are shown with the API's deterministic notice code
(`CREDIT_READ_REQUIRED`, …) translated to text, and free text from the server is never rendered as a notice.

## Alternatives considered

- **Backend-for-frontend (tokens never in the browser, session cookie).** Stronger against XSS token theft and the
  likely production target for a bank. Not chosen for M1 because it adds a stateful service before the agent
  exists; the agent service (M2) is the natural place to host a BFF later. Mitigations now: 5-minute access
  tokens, session storage only, strict CSP, token sent to same-origin API paths only.
- **keycloak-js / keycloak-angular.** Ties the UI to one vendor's adapter; the certified generic OIDC client keeps
  the port replaceable.
- **CORS to each API.** Two more origins to configure and a broader token audience in the browser; the proxy avoids both.

## Verification

- Unit tests (Vitest): domain rules, use cases with fake gateways, problem mapping, claim mapping, runtime config,
  interceptor scope, notice texts. Layer rules: `node --test`.
- Browser tests (Playwright) through the **real Keycloak 26.4 login**: advisor, analyst, approver and admin see
  different data on the same pages; deep link returns after login; token never leaves `/api/`; logout ends the
  Keycloak session; no CSP violations. In CI they run against the full Compose stack.
