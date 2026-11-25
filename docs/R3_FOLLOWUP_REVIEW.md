> **Historical milestone / patch note.** This file documents an earlier revision and may include obsolete defaults or expected test counts. For the CURRENT reference release, see [`README`](../README.md), [`architecture`](architecture.md), [`quality gate`](PUBLICATION_QUALITY_GATE.md) and [`validation status`](VALIDATION_STATUS.md).

# M1 R3 follow-up review

## Evidence reviewed

This review inspected the submitted R3 archive, including `compose.yaml`, the
PostgreSQL schemas, API handlers, Keycloak resource-server settings and the CI workflow.
It did not execute `dotnet`, PostgreSQL, Docker or Keycloak, which are unavailable
in this review environment. Previously reported test counts have not been independently rerun.

## Confirmed changes in the source

- `credit_api` has no UPDATE grant on credit applications. The separate
  `credit_approval` role is NOLOGIN and holds column-scoped UPDATE rights.
- `/health/ready` checks the database and fetches the Keycloak discovery document.
- `make down` retains volumes; `make reset` removes them.

## CI correction applied

- Ensure `TestResults/` exists before Bruno's `--reporter-junit` writes a report.
  A clean Compose job has no output folder from the separate unit-test job.
  Bruno CLI requires the report output directory to exist.

## Still pending live verification

- NuGet restore, compilation against real NuGet packages and xUnit execution.
- Keycloak realm import, issued claims, JWKS access and JWT validation by running APIs.
- Bruno collection against real services rather than a contract mock.
- Docker health probes and networking.
- Angular login and UI are not yet present.

## Additional improvement for a future revision

The readiness probe currently verifies that Keycloak's discovery document
answers successfully. It does not fetch or validate the JWKS document.
Therefore this check establishes identity-provider discovery reachability,
not the complete availability of signing keys. After live stack verification,
consider a JWKS-aware readiness probe (or an explicit JWT signing-key smoke test)
and cover failure cases with real integration tests.

The credit assignment projection is seeded but not actively synchronized;
revocation latency remains an M4 limitation.

## Follow-up applied (round 4)

- **Bruno report directory:** confirmed as a real defect. With Bruno CLI 4.3.0 a missing
  `--reporter-junit` directory makes the run exit with code 2 even when every test passes, so the
  `compose` job would always have failed. The `mkdir -p TestResults` step is kept.
- **JWKS-aware readiness:** implemented. `/health/ready` now reads `jwks_uri` from the discovery
  document and requires a non-empty key set, over the same backchannel path as token validation.
  Covered by `ReadinessTests` (keys present, empty key set, JWKS failure, malformed discovery,
  unreachable discovery). Whether running APIs validate real Keycloak tokens is still proven only by
  the live stack (smoke and Bruno in CI).
