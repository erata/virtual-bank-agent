# ADR-0014: Human-confirmed changes and core banking operations

**Status:** Accepted (2026-11-25). Updates ADR-0005, ADR-0006, ADR-0009, ADR-0011 and ADR-0013.

## Context

Earlier milestones kept every state change out of reach: no approval route (planned as "M6"), the `credit_approval`
role `NOLOGIN`, assistant write tools always refused, customer self-service read-only. That proved the authorization
chain for reads but left roles without the operations that make the authorization model meaningful (decide a credit,
move money, book cash). The same changes must be possible from menu screens and from the assistant without giving the
model any power of its own.

## Decision

1. **One confirmation mechanism for every change.** A use case first *prepares* a `PendingAction` (validation and
   authorization only, no side effect) and returns a card (facts, kind, expiry). Execution happens only through
   `POST /api/actions/{id}/confirm` by the **same principal**, **once**, within **5 minutes**. Someone else's attempt is
   answered "not found" and leaves the card usable for its owner. Each `IConfirmableAction` re-authorizes with the
   current token and data before executing.
2. **The assistant only prepares.** Write tools have risk `PreparesConfirmation`; their result is a card shown under
   the answer. There is no confirm tool, so prompt injection can at most produce a card the user can discard.
3. **Credit decisions (completes "M6").** Approve (optional comment) and reject (reason required) under the separate
   `credit_approval` login role. RLS `USING` is identical for both so permissive policies cannot widen each other;
   limit, `SUBMITTED` state and four-eyes are enforced in the domain, the policy and the database.
4. **Money movement.** Balances change only through bookings (`IAccountBookingRepository`): one database transaction
   per operation, `CHECK (balance >= 0)`, RLS for *who may book which account* (`customer.payment.write` own accounts,
   `account.cash` branch cash desk). Transfers to another customer of the bank credit the beneficiary in the same
   transaction through a narrow `SECURITY DEFINER` function that requires the matching debit.
5. **Customer credit applications** (`credit.request`) are created `SUBMITTED` and prepared by the customer, numbered by
   a sequence, readable only by their preparer (plus approvers), and decided through point 3.
6. **Module boundaries hold.** Credit asks "which customer is the caller?" through `ICreditApplicantDirectory`,
   implemented by an integration adapter outside both modules.

## Consequences

* Every role has meaningful actions; the security chain is visible for writes as well as reads.
* Route allowlist: decisions may only be *prepared* by route; exactly one confirm route executes anything.
* Prepared cards are in memory (single API instance): a restart discards unconfirmed cards. Durable cards, an
  immutable audit store and execution of planned payments on their due date are not implemented.
* This is still not a banking system: no rails, FX, interest, KYC/AML or ledger (see README "Purpose and scope").

Details per operation: [Banking operations](../BANKING_OPERATIONS.md).
