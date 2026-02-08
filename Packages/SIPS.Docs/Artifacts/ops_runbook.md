# SIPS Connect: CheckStatus Reconciliation Runbook (Ops)

## 1. Overview
A transaction enters the `CheckStatus` state when SIPS Connect encounters a **CoreBank Timeout** or **Connectivity Failure** during a callback. This state represents a "Financial Freeze" where the outcome at the participant bank is unknown (in-doubt).

## 2. Identification
Use the following SQL to identify transactions requiring reconciliation:

```sql
SELECT "TxId", "EndToEndId", "Date", "CoreBankResponse"->'auditLedger'
FROM "ISOMessages"
WHERE "Status" = 10; -- CheckStatus
```

## 3. Investigation Workflow

### Step 1: External Verification
Check the **Participant CoreBank (CBS) UI/Logs** for the specific `TxId` or `EndToEndId`.
- **Scenario A**: The transaction was successfully posted in the CBS.
- **Scenario B**: The transaction does not exist or was reversed/failed in the CBS.

### Step 2: Audit Ledger Review
Examine the `auditLedger[]` in SIPS Connect to confirm why `CheckStatus` was raised (e.g., `elapsedMs` > 3000ms).

## 4. Reconciliation (Closure)

> [!IMPORTANT]
> **NEVER** manually update the `Status` column directly. Reconciliation MUST be performed by appending a closure event to the audit ledger via the SAF utility or authorized API.

### Path A: Transaction was CREDITED in CBS
If the bank has already moved the money, append the following event:
- **Event**: `ReconciledCredited`
- **Result**: SIPS Connect records the success as the final adapter-level outcome. No further action needed.

### Path B: Transaction was NOT CREDITED in CBS
If the bank did not move the money, append the following event:
- **Event**: `ReconciledNotCredited`
- **Result**: SIPS Connect confirms the `RJCT` (Reject). The participant is now safe from double-posting risk.

## 5. Critical Safety Rules
1. **NO AUTO-REFUNDS**: Never issue a refund for a `CheckStatus` item without first completing this reconciliation.
2. **NO TRUNCATION**: The audit ledger must remain intact; it is the primary evidence for regulator audits.
3. **IDEMPOTENCY**: If a reconciliation attempt fails, retry using the same `eventId` to ensure audit atomicity.

## 6. Escalation
If a transaction appears in `CheckStatus` but the CBS logs show a "Success" while the IPS records a "Reject" (and settlement has occurred), escalate to the **Financial Audit Team** for manual ledger adjustment.

---

# 🔐 Gold Pattern Operations Addendum

*(Add to `ops_runbook.md`)*

## Scope

This section documents **runtime behavior, incident handling, and operator actions** for inbound IPS messages protected by the **Gold Pattern (INSERT-First Structural De-duplication)** in SIPS Connect.

Applies to:

* Incoming payments (`pacs.008`)
* Incoming verification (`acmt.023`)
* Returns (`pacs.004`)
* Status inquiries (`pacs.028`)

---

## 1. Idempotency Guarantees (Operator View)

### What is guaranteed

* **One IPS instruction → at most one CoreBank call**
* **Deterministic replay** for all IPS retries
* **No duplicate credits** even under concurrent retry storms
* **Audit-traceable ownership** (TxId or MsgId)

### What is not guaranteed

* If a **participant sends a new TxId/MsgId**, the system treats it as a **new instruction**
* CoreBank posting *after* timeout cannot be prevented — only **detected and frozen**

---

## 2. Duplicate Handling – Expected Behavior

### Duplicate Received (TxId or MsgId)

| Scenario                   | System Action                        | Operator Action |
| -------------------------- | ------------------------------------ | --------------- |
| Response already persisted | Replay stored response               | None            |
| Processing still Pending   | 500ms wait → replay or reject        | None            |
| Still Pending after wait   | `admi.002 DuplicateMessageInProcess` | None            |

**Important:** Operators must **never retry manually** for duplicate-in-process events.

---

## 3. CheckStatus (In-Doubt) Transactions

### When CheckStatus is raised

* CoreBank timeout (>3s)
* CoreBank connectivity failure
* Uncertain business outcome

### System behavior

* Transaction marked `CheckStatus`
* Audit event emitted:
  `CheckStatusRaised [POTENTIAL_PHANTOM_CREDIT]`
* **Returns/refunds blocked** automatically for that TxId

### Operator action (MANDATORY)

1. Confirm **CoreBank ledger outcome**
2. Manually reconcile transaction
3. Clear CheckStatus flag **only after confirmation**
4. [See CheckStatus Reconciliation Guide](#sips-connect-checkstatus-reconciliation-runbook-ops)

⚠️ **Never process a return/refund while CheckStatus is active**

---

## 4. Error Taxonomy (For Support & Incident Teams)

### admi.002 — Technical / Protocol Errors

Examples:

* Invalid XML
* Signature failure
* Missing mandatory fields (TxId / MsgId)
* Duplicate message in process
* Database/persistence failure

**Action:**
➡️ Inform participant to correct request and resend
➡️ No financial investigation required

---

### pacs.002 / acmt.024 — Business Outcomes

Examples:

* Insufficient funds
* Invalid account
* System unavailable (CoreBank)
* Verification success/failure

**Action:**
➡️ Follow standard payment/verification support workflow

---

## 5. Audit & Forensics

### Key audit attributes

* `duplicateBy`: `TxId` or `MsgId`
* `UETR`
* `EndToEndId`
* `BizMsgIdr`

### Typical audit events

* `OwnerClaimed`
* `FollowerDuplicateDetected`
* `ReplayServed`
* `DuplicateMessageInProcessRejected`
* `CheckStatusRaised`

These records are **authoritative** during:

* Participant disputes
* Regulator inquiries
* Financial reconciliation

---

## 6. Monitoring Signals (Recommended)

Operations should monitor:

* `ISO_PATH=ReplayStored`
* `ISO_PATH=DuplicatePending`
* `ISO_PATH=CheckStatusRaised`
* `duplicateBy=TxId|MsgId`
* Spike in `admi.002 DuplicateMessageInProcess`

> [!NOTE]
> **Baseline**: Sustained `DuplicatePending` spikes during participant retry storms are expected behavior and **non-actionable**. Only investigate if accompanied by `CheckStatusRaised`.

---

## 7. When to Escalate

Escalate to Engineering only if:

* CheckStatus transactions cannot be resolved within SLA
* CoreBank posts after a confirmed reject
* Database uniqueness constraints fail to enforce (extremely rare)

---

## Final Sign-Off Note

> The Gold Pattern is a **safety mechanism**, not an error condition.
> Duplicate traffic is normal in IPS environments and is **fully neutralized** by design.
