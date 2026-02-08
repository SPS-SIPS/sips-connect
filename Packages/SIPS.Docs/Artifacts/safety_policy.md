# SIPS Connect: No Double Payment Guarantee Policy

## 1. Objective
This policy formalizes the "Negative Loss Guarantee" enforced by **SIPS Connect** acting as a Participant Integration Gateway. It ensures that no technical failure, retry, or timeout within the integration layer can lead to double debits, double credits, or wrongful refunds.

## 2. Structural Safeguards

### 2.1 Idempotency Invariance
SIPS Connect enforces strict Southbound and Northbound idempotency using `X-Idempotency-Key` and `X-Transaction-Id`.
- **Policy**: Any request with an identical idempotency key MUST result in an identical outcome (Return cached response) rather than a second financial instruction.
- **Scope**: Covers all CoreBank callbacks and IPS responses.

### 2.2 Financial Freeze (CheckStatus Guard)
Any transaction that enters an "in-doubt" state (due to CoreBank timeouts or connectivity failures) is immediately placed into a **Financial Freeze**.
- **Rule**: No Refund, Return, or Reversal can be initiated for a transaction in the `CheckStatus` state.
- **Enforcement**: Hard-coded blocking in `OutgoingReturnTransactionHandler` and related services, returning a `409 Conflict` (Financial Freeze).

### 2.3 Deterministic Reconciliation
Integrity is preserved by moving uncertainty from the runtime code to the Audit Ledger.
- **Rule**: A transaction remains in "CheckStatus" until a human operator or a verified Settlement Audit Function (SAF) appends a definitive closure event (`ReconciledCredited` / `ReconciledNotCredited`) to the audit ledger.
- **Result**: Automated systems are structurally prevented from guessing at financial outcomes.

## 3. Neutralized Risk Scenarios

| Risk Scenario | Prevention Mechanism |
| :--- | :--- |
| **Retry-Induced Double Debit** | Idempotency key mapping prevents subsequent postings for the same TxId. |
| **Timeout-Induced Wrongful Refund** | `CheckStatus` blocks any return/refund until outcome is reconciled. |
| **Parallel Execution Race** | PostgreSQL `xmin` optimistic concurrency prevents two handlers from both succeeding. |
| **Late CoreBank Success** | Audit ledger records the "In-Doubt" event before the late success occurs, preventing silent loss. |

## 4. Attestation of Enforcement
The SIPS Connect "Negative Loss" posture is enforced by code-level invariants and verified by the 84-test regression suite. The system is designed such that there is no remaining structural path for users or the bank to lose money through the gateway's operation.

---
**Policy Owner**: SIPS Connect Integration Team
**Revision**: 1.0 (Audit-Grade Hardening)
**Date**: February 2026
