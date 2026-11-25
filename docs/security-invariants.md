# Security invariants

Every invariant maps to automated tests. Status reflects what has **actually executed**, not what
is written. Test levels: **U** unit (`tests/unit`), **D** database (`tests/database/rls-tests.sql`),
**I** integration (Testcontainers), **A** API (Bruno), **E** end-to-end (Playwright).

Status legend: ✅ verified · 🟡 implemented, not yet executed in CI · ⏳ planned (milestone)

| ID | Invariant | Enforcement | Tests | Status |
| --- | --- | --- | --- | --- |
| I1 | LLM cannot authorize operations | `assistant.use` policy, `ToolPolicyGate`, tools only over use cases | U: matrix `*_ASSISTANT_*`; `AssistantTurnTests` (scripted hostile model), `ToolPolicyGateTests`, `Readme_persona_table_holds`; Arch: `Assistant_reaches_modules_only_through_application_use_cases`; A: Bruno `04-assistant` | ✅ unit (mutation-checked) / 🟡 API (CI) |
| I2 | Downstream APIs enforce access independently | Policy re-check of every RLS row in each API | U: `Row_from_other_branch_is_hidden_even_if_rls_returns_it`, `List_drops_rows_the_policy_denies`, `Unassigned_application_returned_by_a_broken_store_is_hidden_and_audited` (Credit use case); A: Bruno `02-customers`, `03-credit` | ✅ handler / use-case level / 🟡 HTTP (CI) |
| I3 | Unauthorized content never reaches model context | Secure retriever | I, evals | ⏳ M3 |
| I4 | APIs validate issuer, signature, lifetime, audience | `AddKeycloakJwtAuthentication` (RS256 only, per-API audience) | A: `no-token`, `wrong-audience`; smoke `WRONG_AUDIENCE` | 🟡 implemented, runs in CI |
| I5 | DB access fails closed without trusted context | RLS helper functions | D: `NO_CONTEXT_FAILS_CLOSED`, `CREDIT_NO_CONTEXT_FAILS_CLOSED`, `APPROVAL_WITHOUT_LIMIT_FAILS_CLOSED` | ✅ |
| I6 | Cache and conversation are principal-isolated | Agent | U, I | ⏳ M4 |
| I7 | Revocation follows documented consistency | Token lifetime, ACL freshness | I | ⏳ M4 |
| I8 | Admin roles grant no business data | Permission catalog + policies | U: matrix `ADMIN_*`, `SECURITY_*`, `AUDITOR_*`; `Administrative_role_sets_category_but_grants_no_business_data` | ✅ |
| I9 | Token exchange cannot create business access | Keycloak + API | I | ⏳ M5 |
| I10 | Prompt injection cannot bypass deterministic authorization | ToolPolicyGate; principal captured server-side in `ToolSession`; undeclared arguments dropped | U: `Undeclared_arguments_cannot_change_whose_rights_are_used`, `Malformed_identifiers_are_rejected_before_any_data_access`, `Notices_are_never_derived_from_model_text`; A: `anna-injection`; LLM evals | ✅ unit / ⏳ evals M6 |
| I11 | State-changing operations need human confirmation | Single-use, owner-bound, 5-minute confirmation cards (`PendingActions`, `ActionConfirmation`); every handler re-authorizes with current data | U: `CreditApprovalTests`, `SelfServiceTests` (transfer/expiry/foreign confirm), `AssistantConfirmationCardTests`, `CashDeskAndBookingTests`, `CreditRequestTests`; Arch: `RouteRuleTests` (one confirm route) | ✅ unit / live-checked |
| I12 | ACL inheritance and explicit deny are enforced | Document policy | U, I | ⏳ M3 |
| I13 | Confidential metadata is not disclosed | Disclosure modes, notices | U: `Disclosure_none_produces_no_notice` | ✅ (core) / ⏳ M3 (retrieval) |
| I14 | Runtime DB roles cannot bypass RLS | Role attributes, FORCE RLS | D: `RUNTIME_ROLES_CANNOT_BYPASS_RLS`, `OWNER_SUBJECT_TO_FORCED_RLS`, `ALL_*_TABLES_FORCE_RLS`, `RUNTIME_ROLE_READ_ONLY`, `CREDIT_API_ROLE_CANNOT_UPDATE`, `WRONG_ROLE_CANNOT_CONNECT_*` | ✅ |
| I15 | Secrets and sensitive content do not leak through logs | Audit logs IDs and reason codes only; `IncludeErrorDetails=false` | I | 🟡 by design, test pending |
| I16 | Query language cannot change permissions | Secure retriever | evals | ⏳ M3 |
| I17 | Query expansion cannot bypass authorization | Secure retriever | U, evals | ⏳ M3 |
| I18 | Conversation history cannot bypass current access | Agent | I | ⏳ M4 |
| I19 | Stale external ACLs fail closed | ACL freshness | I | ⏳ M4/M7 |
| I20 | Machine identities cannot exceed their permissions | Catalog + policies; machine type only from the signed `principal_type` claim | U: matrix `*_SERVICE_*` (planned modules: design + `NO_POLICY` at runtime); `Machine_type_comes_from_the_principal_type_claim`, `A_human_is_never_a_machine_even_with_a_service_like_name`, `Machine_claim_is_only_issued_by_clients_that_cannot_log_in_humans`, `Machine_claim_from_an_untrusted_client_fails_closed`, `Trusted_machine_clients_match_the_realm_in_every_service`; claims script `SERVICE_ACCOUNT_MACHINE_TYPE`, `SERVICE_ACCOUNT_AUTHORIZED_PARTY`, `ANNA_NOT_A_MACHINE` | ✅ unit / 🟡 Keycloak (CI) |
| I21 | Group matching is segment-aware and directional | `GroupPath` | U: `Matching_is_segment_aware`, `Matching_is_directional`, `Malformed_paths_are_rejected` | ✅ |
| I22 | Analysts reach only customers of their assigned applications | Policy + RLS | U: matrix `ANALYST_*`; D: `CUSTOMER_ASSIGNMENT_PATH_MEHMET`, `CUSTOMER_UNASSIGNED_DENIED`, `ASSIGNMENT_*_DENIED` | ✅ |
| I23 | Local ACL changes are not overwritten by filesystem sync | Ingestion | I | ⏳ M3/M4 |
| I24 | External source ACL authority is respected | Connectors | I | ⏳ M7 |
| I25 | Side-effect tools need confirmation | Write tools have risk `PreparesConfirmation`: they return a card and change nothing; the model has no confirm tool | U: `A_note_proposed_by_the_model_is_a_card_and_nothing_is_written_until_the_user_confirms`, `Approval_is_never_executed_from_a_model_turn`, `Hidden_write_tool_cannot_be_executed_by_a_hostile_model` | ✅ unit / live-checked |
| I26 | Business identifiers resolve through APIs, not semantic search | Identifier tools (`get_customer`, `get_credit_application`) with anchored patterns | U: `Identifier_parameters_have_anchored_patterns`, offline routing tests; evals with RAG | ✅ M2 / ⏳ M3 |
| I27 | DB context and projection state do not leak across users or requests | Transaction-local context | D: `NO_LEAK_INTO_NEXT_TRANSACTION`, `CONTEXT_CLEARED_AFTER_COMMIT`, `ASSIGNMENT_PATH_FOREIGN_PRINCIPAL_DENIED`, `PROJECTION_OWN_ROWS_ONLY` | ✅ (single connection) / ⏳ concurrency (I) |

| I28 | Balances never go below zero | `accounts_balance_not_negative CHECK (balance >= 0)`; a failing line rolls back the whole booking | U: `Withdrawal_cannot_exceed_the_balance`; live: overdraft and cross-currency transfers refused | ✅ DB constraint / live-checked |
| I29 | Money is never created by an internal transfer | `customer.credit_internal_beneficiary` (SECURITY DEFINER) requires the payer's matching debit in the same transaction, credits only, max 10,000 | live: direct call without debit refused (`no matching debit in this transaction`) | ✅ DB function / live-checked |
| I30 | Customers book only their own accounts; advisors only their branch's cash desk | RLS `accounts_self_book`, `accounts_cash_desk`, `transactions_*_insert` | U: `Customers_of_another_branch_are_not_found_at_the_cash_desk`, `Only_advisors_with_account_cash_use_the_cash_desk`; live: foreign branch 404, customer on cash desk 403 | ✅ RLS + use case / live-checked |
| I31 | Credit decisions keep four-eyes and limits for rejections too | Identical `USING` for `applications_approve` and `applications_reject`; domain `Reject` uses the approval rules | U: `Four_eyes_applies_to_approval_and_rejection_before_any_card_exists`, `Domain_rejection_follows_the_same_rules_as_approval` | ✅ unit + RLS |

## Additional database guarantees (credit_db)

Verified by `tests/database/rls-tests.sql`: approval over the limit, four-eyes violation, forged
approver and stale-version double approval are rejected **by the database** even if the API's own
check were skipped (`APPROVAL_OVER_LIMIT_DENIED`, `APPROVAL_FOUR_EYES_DENIED`,
`APPROVAL_FORGED_APPROVER_DENIED`, `APPROVAL_STALE_VERSION_NO_EFFECT`).

## What these tests do not prove

- The unit matrix uses **simulated** Keycloak token claims built from the realm export. That the
  real Keycloak emits the same claims (group full paths, attribute claims, audiences) is verified
  only by identity integration tests against a running Keycloak (pending).
- RLS trusts the context the API sets. It protects against query mistakes, not against a
  compromised API process that sets a false context (see [threat model](threat-model.md)).
- The projection is seeded, not synchronized, in M1. Assignment revocation is not yet guaranteed.

## Mutation checks performed

To confirm the suites can fail, policies were deliberately broken once and restored:

| Mutation | Detected by |
| --- | --- |
| Customer RLS branch policy without the permission condition | D: `BRANCH_WITHOUT_PERMISSION_DENIED` |
| Credit RLS approval policy without the four-eyes condition | D: `APPROVAL_FOUR_EYES_DENIED` |
| `CustomerReadPolicy` ignoring the branch | U: `CUSTOMER_OTHER_BRANCH` |
| `CreditApprovePolicy` ignoring the preparer | U: `CREDIT_FOUR_EYES`, `Four_eyes_is_checked_before_the_limit` (policy and domain) |
| Credit Application layer gaining an ASP.NET or database-driver dependency | Arch: `Application_depends_only_on_model`, `Inner_layers_are_plain_class_libraries` |
| A business type added to the shared authorization core | Arch: `Authorization_engine_contains_no_business_types` |
| UI sends the access token to another origin or a non-API path | UI unit: `bearerTokenInterceptor` specs; browser: `the access token is sent to the APIs only` |
| UI renders server text as a notice (injection) | UI unit: `never surfaces free text from the server as a notice code` |
| Inline script / XSS foothold in the UI | nginx CSP `script-src 'self'`; browser: `the app runs under the strict Content-Security-Policy without errors` |
| UI presentation code reaching HTTP, OIDC or storage directly | UI layer rules (`architecture/layer-rules.test.mjs`) |
| An extra route (e.g. `PUT …/decision`) added to the API | Arch: `Api_exposes_exactly_the_allowlisted_routes_and_no_credit_approval` (route allowlist) |
| Customers module code using Credit code (or vice versa) | Arch: `Module_types_do_not_reference_the_other_module`, `Module_source_files_do_not_import_the_other_module` |
| Blank approver or unknown status | D: `BLANK_APPROVER_REJECTED`, `UNKNOWN_STATUS_REJECTED`; U: `Rehydrate_rejects_a_blank_approver`, `Rehydrate_rejects_an_undefined_status` |
| Approved application stored without approval timestamp | D: `APPROVED_REQUIRES_TIMESTAMP`; U: `Rehydrate_rejects_an_approved_application_without_timestamp` |
| Customers module serving rows the policy denies | U: 4 `CustomerUseCaseTests` |
| Contract mock leaking a Munich customer to Anna | A: Bruno `anna-list`, `anna-munich-hidden`, `anna-accounts-hidden` |
| `ToolPolicyGate` without the permission check | U: `Approval_is_never_executed_from_a_model_turn(mehmet.kredit, …)` |
| `ToolPolicyGate` without the call budget | U: `Call_budget_counts_every_request_including_denied_ones` |
| `ToolPolicyGate` without the write limit | U: `Only_one_write_per_turn` |


### M3 document defense-in-depth source mapping (requires live CI verification)

- I2/I3: `SecureDocumentSearch` reauthorizes RLS-filtered `DocumentSearchCandidate` entries using
  `DocumentReadPolicy` and records any policy/RLS mismatch. `DocumentSecurityTests` injects a
  forbidden candidate to verify that it cannot enter the returned model context.
- I3/I12/I19: `scripts/test/document-rls-tests.sh` includes positive Anna/Mehmet/Sophie/Oliver reads,
  clearance and branch negative cases, no-context fail-closed checks, and a user deny overriding
  an actually matching public allow. It requires a real indexed pgvector DB.
- I20: `scripts/test/keycloak-claims.sh` now checks `svc-ingestion`'s real token audience includes
  `virtual-bank-api` in addition to `principal_type=machine`; Keycloak live import must verify this.
- The new tests **do not constitute a passed real integration run** until executed in CI.
