# IT Operations Incident Runbook

If a banking infrastructure component fails, record the incident identifier, impact, mitigation and recovery evidence. Operations operators can access incident runbooks. Business record data should never be included in diagnostic traces without independent authorization.

## Incident classification

A Keycloak outage can prevent new logins without necessarily invalidating every cached session immediately. A database outage affects customer, credit and document queries according to the connection in use. An embedding outage affects ingestion and semantic search, not the authority of existing customer balances. Incident responders must describe these failure domains separately.

## Safe response procedure

Record the incident timestamp, impacted environment, correlation identifiers and recovery steps. Do not paste production access tokens into public issue trackers. Collect sanitized application logs, PostgreSQL health signals, Keycloak readiness status and provider error codes. Avoid deleting a Docker volume while triaging a transient start-up or port conflict.

## Operational examples

If PostgreSQL cannot bind to host port 5432, check which local process owns the port before stopping services. If Keycloak is starting, inspect its health endpoint and realm-import messages. If a cloud embedding provider returns HTTP 429, apply a bounded retry policy and monitor cost; do not silently switch to a different embedding model in an existing pgvector index.

## Authorization on incident data

The operations role can read this operational runbook but does not have access to HR salary bands, credit records or customer payment history. Even emergency response should use specific, time-bounded audited privileges rather than assuming that all administrative accounts can read every bank document.
