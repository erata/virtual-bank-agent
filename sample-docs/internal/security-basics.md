# Security and Authorization Handbook

Every API must validate authentication and independently enforce the necessary business permissions. The LLM never decides authorization. ToolPolicyGate is a pre-check, and the application use case plus PostgreSQL RLS independently restrict data.

## Trusted identity boundaries

A valid JWT is only the beginning of authorization. The backend validates issuer, audience, signature and expiration, then constructs a trusted principal context. Roles are mapped to application permissions by a server-owned catalog. Permissions never come from an LLM, a query string or a document body.

## Defense in depth for tools

ToolPolicyGate permits only registered, low-risk tools and validates call budget, parameter schemas and the caller's permissions. Business use cases perform additional resource-specific checks. PostgreSQL Row-Level Security constrains the rows returned to the application. A missing principal must fail closed instead of returning general bank records.

## Different readers, different answers

A platform administrator may configure system components without gaining read access to credit or HR files. An HR specialist may read a restricted salary policy but cannot see a credit-scoring document. A customer advisor may read the clients of an assigned branch; a credit analyst instead follows assignment relationships. Authorization tests need both positive and negative cases.

## Auditing and trace minimization

Audit logs can record a correlation ID, tool name, decision code and policy outcome. They should not include bearer tokens, raw identity secrets, full private documents or entire account payloads. A durable audit store is a separate implementation milestone and must not be presumed complete merely because an application logger exists.
