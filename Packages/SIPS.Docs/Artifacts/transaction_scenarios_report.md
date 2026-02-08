# SIPS Connect: Transaction & Return Scenarios (Management Report)

**Date**: February 8, 2026  
**Status**: ✅ Phase 5 Switch-Grade Complete  
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
    - **Behavior**: SIPS buffers the payment locally and responds with protocol-level acceptance (not final settlement) to IPS immediately. The CoreBank credit is triggered only *after* final settlement.
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
**Grade: 🏆 GOLD**

| Scenario | System Handling | Integrity |
| :--- | :--- | :--- |
| **Status Re-delivery** | Detected via **Composite Unique Constraint** `(Role, Status, MsgId)`. Previous response replayed. | **GOLD** Idempotent. |
| **Late Confirmation** | Updates a `Pending` transaction to `Success` and triggers the CoreBank "Late Binding" transfer. | Standard Flow. |

---

## 📉 Assessment of Residual Risk

1.  **Manual Reconciliation (The "Ops Burden")**: While the system is financially safe (it freezes instead of failing), `CheckStatus` requires human intervention. This is by design to ensure 100% accuracy in in-doubt scenarios.

---

## 🏆 Spec-Visible Gap Closure: "Switch-Grade Complete"

To ensure full alignment with the **SmartVista IPS Payment Specification**, the following functional enhancements are integrated into the SIPS Connect lifecycle:

### 1. Payment Returns (`pacs.004`)
*   **Inbound Returns**: Protected by the **Gold Pattern**. If the optional `RtrId` is missing, SIPS derives a **Deterministic De-duplication Key** (SHA256) from original transaction attributes to ensure exactly one credit reversal in the CoreBank.
*   **Outbound Returns**: Enforced strict eligibility (Original transaction must be `Success` or `ReadyForReturn`). **`OrgnlTxId`** is mandated as the authoritative anchor (Anchored per SmartVista spec).

### 2. Status Investigations (`pacs.028`)
*   **Protocol Standard**: All Store-and-Forward (SAF) recovery flows now explicitly use **pacs.028.001.05**.
*   **Ledger-as-Truth**: Inbound inquiries are resolved strictly against the **Participant Ledger** (SIPS Connect DB), maintaining participant authority without proxying to CoreBank.
*   **Dual-Initiator Recovery**: Supports investigations triggered by both the sender and the receiver sides.

### 3. Completion Handshake (`pacs.002`)
*   **Notify + Ack Loop**: Distinguishes between standard status reports and final completion notifications using a dedicated **Role Discriminator** (`Pacs002Role`).
*   **Idempotency**: Creditor-side signature and ACK handshake ensure that IPS/Participant ledger synchronization is atomic and non-redundant.

### 4. Verification Hardening (`acmt.024`)
*   **MsgId Sovereignty**: Adheres to the "Message Check" model, using `MsgId` as the strict idempotency key with no fallback to `TxId`, preserving protocol integrity.

---

## 🏁 Final Integrity Attestation
This system is now architected to be **"Switch-Grade Complete."** Every message transition is governed by **TxId atomicity for financial flows** and **MsgId sovereignty for verification flows**, with **Deterministic Replay** enforced throughout.

**Technical Confidence**: 🟢 HIGH (Production-Ready)
