# Inbound Payment Hardening: The "Gold Pattern" for Structural De-duplication

This plan defines the final technical invariants required to ensure SIPS Connect (Participant Integration Gateway) provides a "No Double Credit" guarantee, even under extreme concurrent retries and high latency.

## Design Invariants

### Invariant A: Structural De-duplication (INSERT-First)
**Goal**: One IPS instruction → exactly one CoreBank call.
- **Pattern**: **INSERT-first**, not SELECT-first. Attempt to record the `ISOMessage` immediately.
- **Ownership**: 
    - Success → "Owner" status. Proceed to call CoreBank.
    - `UniqueConstraintViolation` → "Follower" status. Load the existing record.
- **Deterministic Replay (Follower Logic)**:
    - If `ResponseXml` is present: Return immediately.
    - If `Status == Pending`: **Wait 500ms**, re-fetch.
    - If still `Pending`: Return **signed RJCT** (Reason: `Duplicate/Processing`) to avoid hanging the IPS engine.
- **Persistence Guarantee**: Always `PersistResponseAsync` (XML + Status) *before* returning the response to the IPS.

### Invariant B: Phantom Credit & In-Doubt Protection
**Goal**: Protect against "Sender Reject, Receiver Credit" (SLA Timeout).
- **CheckStatus Transition**: CoreBank callbacks > 3s (or failure) raise `CheckStatus`.
- **Audit Ledger**: Append `CheckStatusRaised` with `[POTENTIAL_PHANTOM_CREDIT]` and correlation keys.
- **Freeze**: Block all returns/refunds for that `TxId` until manual reconsideration.

## Proposed Changes

---

### [Component] SIPS.PostgreSQL (Database Layer)

#### [MODIFY] [ISOMessage.cs](file:///Users/maven/source/SIPS/Packages/SIPS.PostgreSQL/Models/ISOMessage.cs)
- Add `public string? UETR { get; set; }`.
- Update `ISOMessageConfiguration` to enforce `UNIQUE (MessageType, TxId)`.

#### [MODIFY] [IIncomingRecorder.cs](file:///Users/maven/source/SIPS/Packages/SIPS.PostgreSQL/Interfaces/IIncomingRecorder.cs)
- Add `Task<ISOMessage?> TryRecordIncomingTransactionAsync(ISOMessage entity, CancellationToken ct);`.
- Implement in `IncomingRecorder.cs` to catch `DbUpdateException` and return the existing record from the DB.

---

### [Component] SIPS.ISO20022 (Schema & Parsing)

#### [MODIFY] [PaymentRequestBuilder.cs](file:///Users/maven/source/SIPS/Packages/SIPS.ISO20022/Helpers/PaymentRequestBuilder.cs)
- Update `Parse` to extract `UETR`.

---

### [Component] SIPS.Core (Business Logic)

#### [MODIFY] [IncomingTransactionHandler.cs](file:///Users/maven/source/SIPS/Packages/SIPS.Core/Services/IncomingTransactionHandler.cs)
- Implement **INSERT-First** logic.
- Implement **Replay-Wait** logic (500ms wait for `Pending`).
- Use `TxId` for `X-Idempotency-Key` Northbound to CoreBank.
- **Signed RJCT (InvalidReference)** if mandatory `TxId` is missing from IPS message.

#### [MODIFY] [IncomingVerificationHandler.cs](file:///Users/maven/source/SIPS/Packages/SIPS.Core/Services/IncomingVerificationHandler.cs)
- **CRITICAL REFACTOR**: Use `MsgId` as the primary idempotency key (SmartVista "Message Check").
- Use `TryRecordIncomingVerificationAsync` (INSERT-First).
- Implement Replay-Wait logic for `Pending` state (500ms).
- **Response Taxonomy**:
    - `admi.002` for protocol/technical errors (ParseFail, Missing MsgId, DuplicatePending).
    - `acmt.024` (Identification Verification Report) for business outcomes (CoreBank Verify Payee).
- Implement `AppendAuditLedgerEventAsync` with `duplicateBy: MsgId`.

#### [MODIFY] [IISOMessageService.cs](file:///Users/maven/source/SIPS/Packages/SIPS.Core/Services/Abstractions/IISOMessageService.cs)
- Add `TryRecordIncomingVerificationAsync` signature returning `(ISOMessage record, DedupOutcome outcome, string? duplicateBy)`.

---

## Verification Plan

### Automated Tests
- `IncomingTransactionHandler_Structural_Dedup_Tests`: Concurrent task storm with same `TxId`. Verify 1 CBS call, identical responses for all.
- `IncomingTransactionHandler_MissingTxId_Tests`: Verify signed ISO rejection.
- `Inbound_CheckStatus_Freeze_Tests`: Verify `CheckStatus` blocks `OutgoingReturn`.

### Manual Proof
- Inspect `ISOMessages` unique index after migration/startup.
