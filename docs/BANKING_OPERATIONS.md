# Banking operations: what each role can do, and how a change is made safely

Virtual Bank supports the core operations of a retail bank on synthetic data. Every **change** follows the same
pattern, whether it starts from a menu screen or from the assistant:

```
prepare  ->  confirmation card  ->  same user confirms  ->  re-check + execute  ->  audit
(no change)   (facts, expiry)        (single use, 5 min)     (policy + RLS + CHECK)
```

* **Prepare** validates the input and the caller's rights and returns a *card* (what will happen, amounts, masked
  IBAN, limit, balance before/after). Nothing has changed yet.
* The card is **single-use**, bound to the **person who prepared it** and **expires after 5 minutes**. Another user's
  attempt is answered "not found" and leaves the card usable for its owner.
* **Confirm** (`POST /api/actions/{id}/confirm`) re-runs every check with the *current* token and data, then executes.
  The database independently enforces the same rules (row-level security, `CHECK` constraints).
* The **assistant can only prepare cards**. It has no confirm tool; the user confirms the card under the answer.

## Operations by role

| Role (demo user) | Operation | Menu | Assistant tool | Permission | Enforced in the database |
|---|---|---|---|---|---|
| Bank customer (`deniz.customer` …) | SEPA transfer dated **today** → booked immediately | My banking → Payments | `plan_transfer` | `customer.payment.write` | own account only (RLS), balance never below 0 (`CHECK`) |
| | SEPA transfer with a **later** date → planned payment | My banking → Payments | `plan_transfer` | `customer.payment.write` | own customer row, amount ≤ 10,000 |
| | Transfer **between own accounts** (same currency) | My banking → Between my accounts | `transfer_between_my_accounts` | `customer.payment.write` | both accounts own, no overdraft, one transaction |
| | **New standing order** (weekly/monthly) | My banking → New standing order | `create_standing_order` | `customer.payment.write` | own customer row, amount ≤ 10,000, active, future date |
| | Cancel a planned payment; pause/resume a standing order | My banking tables | `cancel_planned_payment`, `change_standing_order` | `customer.payment.write` | own rows, only `status`/`active` columns writable |
| | **Apply for a credit** (500–250,000 EUR, 6–360 months) | Credit applications → Apply | `request_credit` | `credit.request` | `prepared_by` = caller, `SUBMITTED`, undecided, bounded amount |
| Customer advisor (`anna.berater`) | **Cash deposit / withdrawal** at the counter | Customer → Cash desk | `cash_deposit`, `cash_withdrawal` | `account.cash` (+ `customer.read`) | customer of the **advisor's branch** only; no overdraft |
| Credit analyst (`mehmet.kredit`) | Add a note to an assigned application | Credit application → Notes | `add_credit_note` (card) | `credit.note.write` | assigned applications only |
| Credit approver (`max.approver`) | **Approve** (optional comment) / **reject** (reason required) | Credit application → Approval | `approve_credit_application`, `reject_credit_application` | `credit.approve` | separate `credit_approval` DB role; `SUBMITTED`, within limit, never own application (four-eyes) |
| Security admin (`sarah.security`) | Change who may read a document | Document access | — | `document.acl.manage` | metadata only, optimistic version check, history |

Roles without a permission do not see the menu or the tool, and the API refuses a direct call anyway.

## Money movement rules

* **Balances change only through bookings.** A booking updates one account balance *and* writes a transaction row in
  the same database transaction (`IAccountBookingRepository`). A transfer between own accounts books both lines in
  one transaction: either both happen or neither.
* **Transfers to another customer of this bank** (the beneficiary IBAN belongs to a Virtual Bank customer) credit the
  beneficiary's account in the **same database transaction** as the payer's debit, via
  `customer.credit_internal_beneficiary` (`SECURITY DEFINER`). It only credits, only up to 10,000, refuses the payer's
  own IBAN, and requires the payer's matching debit to exist in the current transaction, so money cannot be created.
  Other IBANs are external transfers (debit only). The assistant cannot look up another customer's IBAN; it asks.
* **No overdraft.** `accounts_balance_not_negative CHECK (balance >= 0)`; a debit that would break it rolls the whole
  booking back and is reported as *insufficient funds*.
* **No currency conversion.** Transfers between own accounts require the same currency; SEPA transfers are debited
  from an account in the chosen currency.
* **Limits.** 10,000 per transfer, standing order and cash booking; credit applications 500–250,000 EUR. The same
  bounds exist in the UI (early feedback), in the use case and in RLS `WITH CHECK`.
* **Who may book which account** is decided by RLS: `accounts_self_book` (own accounts, `customer.payment.write`) and
  `accounts_cash_desk` (customers of the advisor's branch, `account.cash`).
* **Planned payments** (SEPA transfers dated in the future) are listed under *upcoming payments* and can be cancelled.
  This demo has no scheduler that executes them on the due date; a real bank would book them then, after checking
  cover again.

## Credit lifecycle

```
customer applies (credit.request)  ->  SUBMITTED (prepared_by = customer)
analyst adds notes (assigned applications)
approver approves / rejects (within limit, not own)  ->  APPROVED / REJECTED (+ decision note)
customer sees status and the decision note (never internal staff notes)
```

Numbers for customer applications come from `credit.next_application_id()` (sequence, `SECURITY DEFINER`), because
RLS hides other applications and "max + 1" would collide.

## Where to look in the code

| Concern | Location |
|---|---|
| Confirmation cards (prepare / confirm / discard, expiry, owner binding) | `src/VirtualBank.Application/Common/Actions/PendingActions.cs` |
| Customer payments, internal transfers, standing orders | `src/VirtualBank.Application/Customers/Commands/SelfService/MyPaymentOrders.cs` |
| Cash desk | `src/VirtualBank.Application/Customers/Commands/CashDesk/CashDesk.cs` |
| Credit decisions / customer credit requests | `src/VirtualBank.Application/Credit/Commands/ApproveCreditApplication`, `.../RequestCredit` |
| Bookings (one transaction, overdraft → rollback) | `src/VirtualBank.Infrastructure/Persistence/Customers/PostgresAccountBookingRepository.cs` |
| Database rules | migrations `SecureCustomerSelfServiceWrites`, `SecureCustomerBanking`, `SecureCreditDecisions`, `SecureCreditDecidedVisibility`, `SecureCreditCustomerRequests` |
| Assistant tools (all card-preparing) | `src/VirtualBank.Application/Assistant/Tools/AssistantToolCatalog.cs` |
| Shared confirmation card in the UI | `src/Frontend/virtual-bank-ui/src/app/presentation/shared/action-card.ts` |

## Known limitations (deliberate for a demo)

* Prepared cards live in memory: restarting the API discards unconfirmed cards (confirmed operations are durable).
* After rejecting an application the approver no longer sees it in the list; the analyst and the customer do.
* Customer credit applications are not assigned to an analyst automatically; approvers see them as `SUBMITTED`.
* Transactions are stored per customer; the account is part of the transaction text.
