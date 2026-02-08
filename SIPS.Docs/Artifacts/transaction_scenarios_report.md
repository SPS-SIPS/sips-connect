# SIPS Connect: Transaction & Return Scenarios (Management Report)

**Date**: February 8, 2026  
**Status**: Production-Ready (Hardening Phase 4 Complete)  
**Objective**: This report provides an honest assessment of how SIPS Connect handles various financial scenarios to ensure absolute integrity and regulatory compliance.

---

## 🏗️ Resilience Grading System

*   **🏆 GOLD**: Absolute protection. Immune to retry storms, concurrent race conditions, and phantom credits. (INSERT-First De-duplication).
*   **🥈 SILVER**: Robust protection. Prevents double-processing using state-validation. Safe for production.
*   **🥉 BRONZE**: Standard handling. Functional but lacks high-concurrency "Gold" hardening.

---

## 🛡️ Authorization Modes: Strict vs. Permissive
The system dynamically adapts its safety profile based on the bank's integration capability (`IncludeCoreBankOnListing` flag).

*   **🛠️ STRICT MODE (Real-time Authorization)**:
    - **Behavior**: SIPS calls CoreBank *immediately* during the initial message handshake. 
    - **Risk Mitigation**: Credits are pre-authorized. If CoreBank is down, SIPS rejects the request to IPS.
    - **Best for**: Banks with real-time STP (Straight Through Processing) capabilities.

*   **⚡ PERMISSIVE MODE (Buffered/Late Binding)**:
    - **Behavior**: SIPS buffers the payment locally and responds "Accepted" to IPS immediately. The CoreBank credit is triggered only *after* final settlement.
    - **Risk Mitigation**: Ensures high availability. SIPS protects the bank from network latency during the handshake.
    - **Best for**: Banks requiring an integration buffer or with "Late Binding" settlement models.

---

## 1. Incoming Payments (`pacs.008`)
**Grade: 🏆 GOLD**

| Scenario | Mode | System Handling | Financial Impact |
| :--- | :--- | :--- | :--- |
| **Normal Success** | Strict | Real-time CB check -> ACSC. | Ledger Balanced (Sync). |
| **Normal Success** | Permissive | Local Record -> ACSC. CB credit triggered on completion. | Ledger Balanced (Async). |
| **Retry Storm** | Both | **INSERT-First** logic; followers receive replay. | **ZERO** double-credit risk. |
| **CB Timeout** | Strict | `CheckStatus` raised. `RJCT` returned to IPS. | **PHANTOM CREDIT PREVENTED**. |
| **CB Timeout** | Permissive | Handled in completion flow. `CheckStatus` prevents double-post. | Audit-trail safe. |

---

## 2. Incoming Verification (`acmt.023`)
**Grade: 🏆 GOLD**

| Scenario | System Handling | Integrity |
| :--- | :--- | :--- |
| **New Request** | Recorded via `MsgId`. CoreBank queried. `acmt.024` returned. | High. |
| **Concurrent Retry** | Second request detected via `UNIQUE` constraint. Replay-wait served. | Audit traceable. |
| **Technical Fault** | `admi.002` returned immediately for infrastructure/signing errors. | Compliant. |

---

## 3. Outgoing Payments (Initiated by our Bank)
**Grade: 🥈 SILVER**

| Scenario | System Handling | Financial Impact |
| :--- | :--- | :--- |
| **IPS Timeout** | SIPS Connect marks transaction as `CheckStatus`. CoreBank receives `PDNG`. | Prevents premature reversal. |
| **IPS Reject** | Transaction finalized as `Failed`. Mapping performed for bank-friendly reason codes. | Account unfrozen. |
| **Poll/Sync** | SAF process automatically queries IPS for in-doubt transactions. | Self-healing. |

---

## 4. Payment Status Reports (`pacs.002`)
**Grade: 🥈 SILVER**

| Scenario | System Handling | Integrity |
| :--- | :--- | :--- |
| **Double Completion** | If IPS sends two `pacs.002` for one payment, the system detects the "Non-Pending" status and ignores the second update. | Idempotent. |
| **Late Confirmation** | Updates a `Pending` transaction to `Success` and triggers the CoreBank "Late Binding" transfer. | Standard Flow. |

---

## 📉 Assessment of Residual Risk

1.  **Manual Reconciliation (The "Ops Burden")**: While the system is financially safe (it freezes instead of failing), `CheckStatus` requires human intervention. This is by design to ensure 100% accuracy.
2.  **State-Based Idempotency**: The Status Report handler (`pacs.002`) does not yet use the "Gold" INSERT-First pattern for its own status records. This is acceptable for outcome signals but could be a future "Gold" target to improve audit granularity.

---

## 🏁 Final Conclusion
The SIPS Connect system is now **Hardened for Scale**. The implementation of the **Gold Pattern** (INSERT-First) on all critical inbound entry points ensures that the Participant Integration Gateway remains the "Source of Truth" even during extreme network instability or IPS retry storms.

> [!IMPORTANT]
> **Key Management Takeaway**: The system will always choose to **Freeze (CheckStatus)** over **Guessing**. This prioritizes financial solvency over temporary latency.
