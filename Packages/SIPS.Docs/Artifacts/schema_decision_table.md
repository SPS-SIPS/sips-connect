# ISOMessage Schema & Replay Decision Table

**Purpose**: Protocol-correct mapping validation for SmartVista IPS compliance  
**Date**: February 7, 2026

---

## A) ISOMessage Schema

### Entity Fields

```csharp
public class ISOMessage
{
    // Primary Key
    public int Id { get; set; }
    
    // Message Classification
    public ISOMessageType MessageType { get; set; }  // Enum: TransactionRequest, VerificationRequest, ReturnRequest, StatusRequest
    public TransactionStatus Status { get; set; }    // Enum: Pending, Success, Failed, CheckStatus
    
    // SmartVista Identifiers
    public string MsgId { get; set; }                // Message-level unique ID (for message check)
    public string BizMsgIdr { get; set; }            // Business message identifier (AppHdr)
    public string MsgDefIdr { get; set; }            // Message definition identifier (e.g., pacs.008.001.10)
    public string? TxId { get; set; }                // Transaction-level unique ID (PmtId/TxId)
    public string? UETR { get; set; }                // Universal End-to-End Transaction Reference
    public string? EndToEndId { get; set; }          // End-to-end identifier
    
    // Processing State
    public int Round { get; set; } = 1;              // Retry/SAF round counter
    public string? Reason { get; set; }              // Rejection/failure reason
    public string? AdditionalInfo { get; set; }      // Additional context
    
    // Timestamps
    public DateTimeOffset Date { get; set; }         // Message creation timestamp
    
    // Participants
    public string FromBIC { get; set; }              // Sender BIC
    public string ToBIC { get; set; }                // Receiver BIC
    
    // Payloads
    public byte[] Message { get; set; }              // Original incoming ISO20022 XML
    public byte[]? Response { get; set; }            // Outgoing ISO20022 response XML
    public string? CoreBankResponse { get; set; }    // JSONB: CoreBank callback result + audit ledger
    
    // Return-specific
    public string? ReturnId { get; set; }            // Return transaction identifier (pacs.004)
    
    // Relationships
    public ICollection<Transaction> Transactions { get; set; }
    public ICollection<ISOMessageStatus> Statuses { get; set; }
    
    // Concurrency
    public uint xmin { get; private set; }           // PostgreSQL MVCC concurrency token
}
```

### Current Indexes

```sql
-- Primary Key
CREATE UNIQUE INDEX PK_ISOMessages ON "ISOMessages" ("Id");

-- [CURRENT] Transaction-level de-duplication
CREATE UNIQUE INDEX IX_ISOMessages_MessageType_TxId 
ON "ISOMessages" ("MessageType", "TxId");

-- Secondary correlation
CREATE INDEX IX_ISOMessages_UETR 
ON "ISOMessages" ("UETR");
```

### Proposed Additional Index (MsgId)

```sql
-- [PROPOSED] Message-level de-duplication
CREATE UNIQUE INDEX IX_ISOMessages_MessageType_MsgId 
ON "ISOMessages" ("MessageType", "MsgId")
WHERE "MsgId" IS NOT NULL AND "MsgId" != '';
```

**Rationale**: Partial unique index to handle cases where `MsgId` might be empty/null for certain message types.

---

## B) IncomingTransactionHandler Replay Decision Table

### Current Handler Paths

| Path | Trigger Condition | Current Action | Current Response Type | Status Code |
|------|------------------|----------------|---------------------|-------------|
| **ParseFail** | Signature verification fails OR XML parse fails | Return error message | ❌ Generic error (AdminMessage) | N/A |
| **MissingTxId** | `request.TxId` is null/empty | Build pacs.002 RJCT | ❌ pacs.002 | RJCT |
| **DbInsertFailed** | `TryRecordIncomingTransactionAsync` returns null | Build pacs.002 RJCT | ❌ pacs.002 | RJCT |
| **ReplayStored** | `record.Response` exists (completed transaction) | Replay stored `ResponseXml` | ✅ Original response | Original status |
| **ConcurrentPending** | `record.Status` is Pending/CheckStatus | Wait 500ms, re-fetch | → ReplayAfterWait or DuplicatePending | - |
| **ReplayAfterWait** | Owner completed during 500ms wait | Replay newly stored `ResponseXml` | ✅ Original response | Original status |
| **DuplicatePending** | Owner still processing after 500ms | Build pacs.002 RJCT (Duplicate) | ❌ pacs.002 | RJCT |
| **Normal** (Owner) | First request, INSERT succeeds | Call CoreBank, persist response | pacs.002 | ACSC/RJCT |
| **CoreBankTimeout** | CoreBank call exceeds 3s timeout | Build pacs.002 RJCT, mark CheckStatus | ❌ pacs.002 | RJCT |
| **CoreBankError** | CoreBank connectivity failure | Build pacs.002 RJCT | ❌ pacs.002 | RJCT |
| **EmergencyCatch** | Catastrophic unhandled exception | Return AdminMessage | ❌ Generic error | N/A |

### Detailed Path Descriptions

#### 1. ParseFail
- **Trigger**: `_inbound.VerifyAndParseAsync` returns `isValid = false`
- **Current**: Returns `AdminMessage.Generate("Failed to verify...")`
- **Should Be**: **admi.002** (Reason: InvalidXML or SignatureFailure)

#### 2. MissingTxId
- **Trigger**: `string.IsNullOrWhiteSpace(request.TxId)`
- **Current**: Returns `pacs.002 RJCT` (Reason: "Format Error")
- **Should Be**: **admi.002** (Reason: MandatoryElementMissing)

#### 3. DbInsertFailed
- **Trigger**: `TryRecordIncomingTransactionAsync` returns null (unexpected DB failure)
- **Current**: Returns `pacs.002 RJCT` (Reason: "System Error")
- **Should Be**: **admi.002** (Reason: TechnicalError) OR retry/escalate

#### 4. ReplayStored
- **Trigger**: Follower finds existing `Response` (transaction already processed)
- **Current**: Replays stored `ResponseXml` (preserves original message type)
- **Should Be**: ✅ **Correct** (replay preserves admi.002 or pacs.002 as originally sent)

#### 5. ConcurrentPending
- **Trigger**: Follower finds `Status = Pending` or `CheckStatus`
- **Current**: Waits 500ms, re-fetches
- **Should Be**: ✅ **Correct** (wait logic is appropriate)

#### 6. ReplayAfterWait
- **Trigger**: Owner completes during 500ms wait, follower finds new `Response`
- **Current**: Replays newly stored `ResponseXml`
- **Should Be**: ✅ **Correct** (replay preserves original message type)

#### 7. DuplicatePending
- **Trigger**: Owner still processing after 500ms wait
- **Current**: Returns `pacs.002 RJCT` (Reason: "Duplicate")
- **Should Be**: **admi.002** (Reason: DuplicateMessageInProcess)

#### 8. Normal (Owner Path)
- **Trigger**: First request, INSERT succeeds, proceeds to CoreBank
- **Current**: Returns `pacs.002 ACSC` (pending) or `pacs.002 RJCT` (business reject)
- **Should Be**: ✅ **Correct** (business outcome)

#### 9. CoreBankTimeout
- **Trigger**: CoreBank call exceeds 3s timeout
- **Current**: Returns `pacs.002 RJCT` (Reason: "System Unavailable"), marks CheckStatus
- **Should Be**: ✅ **Correct** (business outcome: system unavailable) OR **admi.002** if treating as technical failure

#### 10. CoreBankError
- **Trigger**: CoreBank connectivity failure (non-timeout exception)
- **Current**: Returns `pacs.002 RJCT` (Reason: "System Unavailable")
- **Should Be**: ✅ **Correct** (business outcome: system unavailable) OR **admi.002** if treating as technical failure

#### 11. EmergencyCatch
- **Trigger**: Catastrophic unhandled exception in outer try-catch
- **Current**: Returns `AdminMessage.Generate("Internal System Error")`
- **Should Be**: **admi.002** (Reason: TechnicalError)

---

## C) Message Type Enum

```csharp
public enum ISOMessageType
{
    TransactionRequest,      // pacs.008 (incoming payment)
    VerificationRequest,     // acmt.023 (payee verification)
    ReturnRequest,           // pacs.004 (return payment)
    StatusRequest            // pacs.028 (status inquiry)
}
```

---

## D) Transaction Status Enum

```csharp
public enum TransactionStatus
{
    Pending,        // Awaiting completion/confirmation
    Success,        // Successfully processed
    Failed,         // Rejected/failed
    CheckStatus     // In-doubt, requires manual reconciliation
}
```

---

## E) Questions for Protocol Mapping

1. **CoreBankTimeout/Error**: Should these be `pacs.002` (business unavailable) or `admi.002` (technical failure)?
   - Current: `pacs.002 RJCT` with CheckStatus
   - Rationale: Treating as business outcome (system unavailable to process payment)

2. **DuplicatePending**: Confirmed as `admi.002` (duplicate message in process)?
   - Current: `pacs.002 RJCT`
   - Proposed: `admi.002` (DuplicateMessageInProcess)

3. **Partial Unique Index**: Should we use `WHERE "MsgId" IS NOT NULL AND "MsgId" != ''` or just `WHERE "MsgId" IS NOT NULL`?

4. **MsgId Conflict Handling**: When `TryRecordIncomingTransactionAsync` catches a MsgId duplicate (not TxId), should we:
   - Replay stored response (same as TxId duplicate)?
   - Return `admi.002` (DuplicateMessageID)?

---

## F) Audit Ledger Events (for reference)

Current audit events appended to `CoreBankResponse.auditLedger`:

```json
{
  "schemaVersion": 1,
  "eventId": "guid",
  "actor": "System",
  "event": "CheckStatusRaised" | "DuplicateReceivedWhilePending",
  "reason": "CoreBankTimeout" | "DuplicateDetected",
  "timestampUtc": "2026-02-07T20:00:00Z",
  "correlation": {
    "transactionId": "TxId",
    "msgId": "MsgId",
    "uetr": "UETR",
    "endToEndId": "EndToEndId"
  }
}
```

---

## Next Steps

Awaiting your protocol-correct mapping matrix for:
1. **admi.002 vs pacs.002** per handler path
2. **Reason codes** for each error type
3. **Index strategy** (partial unique constraints)
4. **MsgId conflict handling** logic
