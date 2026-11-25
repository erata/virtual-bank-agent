# Seeded document-access reference (static oracle, not a live database report)

**Source:** `sample-docs/manifest.json`, `policies/permissions.json`, and `infra/keycloak/realm-virtual-bank.json` from this revision. The exact IDs below were calculated with the independent fixture authorization oracle in `tools/evaluate_rag.py`. This table **does not prove** that Keycloak tokens, PostgreSQL RLS, pgvector or Application policy behaved correctly in a real deployment. Run `scripts/test/document-rls-tests.sh` and the live `tools/evaluate_rag.py` script for that evidence.

This table assumes the initial freshly seeded ACLs only. After a security administrator changes effective ACLs in the database, the database becomes the authoritative source; the static table can no longer describe all current access.

| Persona | Role | Allowed count / 30 | Document IDs (without `DOC-` prefix) |
|---|---|---:|---|
| `anna.berater` | Customer Advisor · STR | 15 | 101, 102, 103, 104, 107, 110, 111, 116, 117, 118, 119, 120, 121, 129, 130 |
| `mehmet.kredit` | Credit Analyst | 21 | 101, 102, 103, 105, 107, 108, 110, 111, 112, 113, 116, 117, 118, 119, 120, 122, 123, 124, 125, 129, 130 |
| `max.approver` | Credit Approver | 20 | 101, 102, 103, 105, 107, 108, 110, 112, 113, 116, 117, 118, 119, 120, 122, 123, 124, 125, 129, 130 |
| `sophie.hr` | HR | 14 | 101, 102, 103, 106, 110, 114, 116, 117, 118, 119, 120, 126, 129, 130 |
| `oliver.ops` | Operations | 15 | 101, 102, 103, 109, 110, 115, 116, 117, 118, 119, 120, 127, 128, 129, 130 |
| `deniz.customer` | Bank Customer | 11 | 101, 102, 103, 110, 116, 117, 118, 119, 120, 129, 130 |
| `thomas.customer` | Bank Customer | 11 | 101, 102, 103, 110, 116, 117, 118, 119, 120, 129, 130 |
| `lena.admin` | Platform Admin | 0 | **None** |
| `sarah.security` | Security Admin | 0 | **None** |
| `tom.auditor` | Auditor | 0 | **None** |

## Why customers can access public docs but not other data

The role `bank-customer` is granted `document.read` **and** its Keycloak clearance is deliberately set to `public` (0). The public document ACL applies only to the public corpus. For example, Deniz can ask about 2026 account fees in DOC-117 and a SEPA transfer in DOC-118. Deniz must never see DOC-105/123 credit policies, DOC-106/126 HR policies, DOC-104/121 internal staff guides, or another customer's account details. The latter are protected by **different** customer data policies and subject-bound RLS, not by document ACL.

Lena's `platform-admin` role has no `document.read`, so it does not grant the ability to search even public documents. Sarah can manage ACL metadata without document contents. Tom has audit-specific permissions, not a universal data read role.

## Positive and negative tests

- **Positive:** Deniz and Thomas can retrieve a public FAQ and the current 2026 fee PDF; Anna sees the Stuttgart branch guide; Sophie sees the restricted HR document; Mehmet sees current and archived credit policies.
- **Negative:** Deniz cannot see internal, credit or HR documents; Anna cannot see branch-restricted non-matching documents; Lena sees zero documents; explicit matching `DENY` overrides matching `ALLOW`.
- **Retrieval ranking:** DOC-116/117 and DOC-122/123 describe similar topics with different effective dates. Seeing both is not a permission failure when the user is authorized; using an **archived** policy as the current rule is a **temporal relevance** failure.

See [RAG evaluation](RAG_EVALUATION.md) for the 42-query live test protocol and the independent authorization oracle.
