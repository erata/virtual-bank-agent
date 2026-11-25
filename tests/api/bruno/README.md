# Bruno API collection (M1)

Git-versioned API tests for the VirtualBank API (customer and credit endpoints). The collection obtains its own
development-only tokens from Keycloak in `01-auth` and stores them as runtime variables, so no
token is ever written to the repository.

```bash
make up            # PostgreSQL, Keycloak, API, web UI
make test-api      # runs: npx @usebruno/cli@4.3.0 run --env local (inside this folder)
```

`01-auth/svc-reporting.bru` fetches a service-account token that carries **no** `virtual-bank-api`
audience; `02-customers/wrong-audience.bru` proves the API rejects it (401).
`03-credit/mehmet-note.bru` writes one note per run; `make reset` resets the database.
