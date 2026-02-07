# Gold Pattern Refinement: SmartVista Protocol Compliance

**Date**: February 7, 2026  
**Status**: Planning Phase  
**Objective**: Harden the Gold Pattern implementation to full SmartVista IPS protocol compliance

---

## Executive Summary

The current Gold Pattern implementation is **structurally sound** and aligned with SmartVista's retry semantics. This plan addresses the critical gaps identified in the SmartVista IPS specification review:

1. **MsgId-based message-level de-duplication** (secondary guard)
2. **Protocol-correct error taxonomy** (admi.002 vs pacs.002)
3. **Verification handler hardening** (acmt.023/024)
4. **Concurrency testing** matching IPS retry patterns

---

## Current State Analysis

### ✅ What's Already Correct

| Component | Status | SmartVista Alignment |
|-----------|--------|---------------------|
| `UNIQUE (MessageType, TxId)` | ✅ Implemented | Transaction-level uniqueness per spec |
| `MsgId` storage | ✅ Already in schema | Message-level check field available |
| `UETR` extraction & indexing | ✅ Implemented | Secondary correlation for audit |
| Persist-before-return | ✅ Implemented | Enables deterministic replay within 10s SLA |
| 500ms follower wait | ✅ Implemented | Reasonable for concurrent pending requests |

### ⚠️ Critical Gaps to Close

1. **MsgId not used for de-duplication**: Currently only indexed, not enforced as unique
2. **Error taxonomy incomplete**: Missing admi.002 for technical/duplicate rejections
3. **Verification not hardened**: acmt.023/024 flows lack INSERT-first pattern
4. **No concurrency tests**: Need to validate against IPS retry patterns

---

## Proposed Changes

### 1. MsgId-Based Message-Level De-duplication

#### Problem
SmartVista spec states:
- `MsgId` = "unique message ID for message check"
- `TxId` = "mandatory unique ID for transaction status requests"

Current implementation only enforces `TxId` uniqueness, leaving a gap for message-level duplicates where `TxId` might be absent or inconsistent.

#### Solution: Composite Uniqueness Strategy

**Option A (Recommended)**: Add secondary unique index on `(MessageType, MsgId)`

```sql
CREATE UNIQUE INDEX IX_ISOMessages_MessageType_MsgId 
ON "ISOMessages" ("MessageType", "MsgId");
```

**Rationale**:
- Provides message-level duplicate detection
- Complements transaction-level `TxId` uniqueness
- Handles edge cases where `TxId` is null/empty
- Aligns with SmartVista's dual-identity model

**Option B (Alternative)**: Composite key `(MessageType, TxId, MsgId)`

```sql
CREATE UNIQUE INDEX IX_ISOMessages_MessageType_TxId_MsgId 
ON "ISOMessages" ("MessageType", COALESCE("TxId", ''), "MsgId");
```

**Decision Required**: Which option aligns with your observed IPS behavior?

---

### 2. Protocol-Correct Error Taxonomy

#### SmartVista Error Taxonomy

| Error Type | ISO Message | Use Case |
|------------|-------------|----------|
| Technical/XML/Signature | `admi.002` | Invalid XML, signature failure, duplicate IDs |
| Business Exception | `pacs.002` | Insufficient funds, invalid account, etc. |
| Duplicate Detection | `admi.002` | Same MsgId/TxId received again |

#### Current Implementation Gap

`IncomingTransactionHandler` returns generic `pacs.002 RJCT` for all errors, including:
- Missing TxId → Should be `admi.002` (Format Error)
- Duplicate MsgId → Should be `admi.002` (Duplicate)
- Parse failure → Should be `admi.002` (Invalid XML)

#### Proposed Mapping

```csharp
// Technical/Protocol Errors → admi.002
- ParseFail → admi.002 (Reason: InvalidXML)
- MissingTxId → admi.002 (Reason: MandatoryElementMissing)
- DuplicatePending → admi.002 (Reason: DuplicateMessageID)
- ReplayStored → admi.002 (if original was reject) or replay original

// Business Errors → pacs.002
- CoreBank RJCT → pacs.002 (Reason: from CoreBank)
- CoreBankTimeout → pacs.002 (Reason: SystemUnavailable, Status: CheckStatus)
```

#### Implementation

1. Create `AdminMessageBuilder` for `admi.002` responses
2. Update `IncomingTransactionHandler` error paths to use correct message type
3. Ensure `ReplayStored` path preserves original message type

---

### 3. Verification Handler Hardening (acmt.023/024)

#### Problem
`IncomingVerificationHandler` currently lacks INSERT-first de-duplication, leading to:
- Duplicate lookups to CoreBank
- Inconsistent UX under retries
- Operational noise

#### Solution: Apply Gold Pattern to Verification

**Changes Required**:

1. **Schema**: Add unique constraint for verification requests
   ```sql
   -- Verification uses SIPSRequestId as the unique key
   CREATE UNIQUE INDEX IX_ISOMessages_MessageType_SIPSRequestId 
   ON "ISOMessages" ("MessageType", "TxId") 
   WHERE "MessageType" = 'VerificationRequest';
   ```

2. **Persistence**: Implement `TryRecordIncomingVerificationAsync`
   ```csharp
   Task<ISOMessage?> TryRecordIncomingVerificationAsync(
       PayeeVerificationBuilder.Request request,
       string rawXml,
       CancellationToken ct);
   ```

3. **Handler**: Update `IncomingVerificationHandler.HandleAsync` with:
   - INSERT-first ownership
   - Replay stored responses
   - 500ms wait for concurrent pending
   - Hard reject missing SIPSRequestId

**Priority**: Medium (after transaction hardening is validated)

---

### 4. Concurrency Testing Requirements

#### Test Suite Design

**Test Class 1: Concurrent Duplicate Detection**
```csharp
[Fact]
public async Task SameTxId_20ConcurrentCallbacks_ExactlyOneCoreBank Call()
{
    // Arrange: Same TxId, 20 parallel requests
    // Act: Fire all 20 concurrently
    // Assert: 
    //   - Exactly 1 CoreBank call
    //   - 1 Owner (new insert)
    //   - 19 Followers (replay stored response)
    //   - All 20 receive identical ResponseXml
}
```

**Test Class 2: Deterministic Timeout Replay**
```csharp
[Fact]
public async Task OwnerTimesOutCoreBank_FollowersReplayStoredRJCT()
{
    // Arrange: First request times out CoreBank
    // Act: Send duplicate while first is CheckStatus
    // Assert:
    //   - Follower replays stored RJCT (SystemUnavailable)
    //   - No second CoreBank call
    //   - Both marked CheckStatus
}
```

**Test Class 3: DB Transient Failure Recovery**
```csharp
[Fact]
public async Task DbFailureDuringPersist_NoDoubleCoreBank Post()
{
    // Arrange: Simulate DB failure after CoreBank success
    // Act: Retry the request
    // Assert:
    //   - No duplicate CoreBank call
    //   - Eventually consistent state
}
```

---

## Implementation Roadmap

### Phase 1: MsgId De-duplication (High Priority)
- [ ] Add `UNIQUE (MessageType, MsgId)` index
- [ ] Update `TryRecordIncomingTransactionAsync` to handle MsgId conflicts
- [ ] Add audit event for MsgId-based de-duplication
- [ ] Test with duplicate MsgId scenarios

### Phase 2: Error Taxonomy (High Priority)
- [ ] Create `AdminMessageBuilder` for admi.002
- [ ] Update error paths in `IncomingTransactionHandler`
- [ ] Ensure replay preserves original message type
- [ ] Document error mapping in ops runbook

### Phase 3: Verification Hardening (Medium Priority)
- [ ] Apply INSERT-first pattern to `IncomingVerificationHandler`
- [ ] Add unique constraint for verification requests
- [ ] Implement replay logic for acmt.024 responses
- [ ] Test concurrent verification requests

### Phase 4: Concurrency Testing (High Priority)
- [ ] Implement Test Class 1 (concurrent duplicates)
- [ ] Implement Test Class 2 (timeout replay)
- [ ] Implement Test Class 3 (DB failure recovery)
- [ ] Run load tests with 100+ concurrent requests

### Phase 5: Database Migration (Mandatory)
- [ ] Generate EF Core migration for all schema changes
- [ ] Apply to development environment
- [ ] Validate migration rollback strategy
- [ ] Document migration in ops runbook

---

## Open Questions for User

1. **MsgId Uniqueness**: Should we enforce `UNIQUE (MessageType, MsgId)` in addition to `(MessageType, TxId)`, or use a composite key?
2. **Error Taxonomy**: Do you want `admi.002` responses for all technical errors, or only specific cases?
3. **Verification Priority**: Should we harden verification handlers immediately, or defer until transaction hardening is validated in production?
4. **Timeout Tuning**: Is 500ms follower wait appropriate for your observed IPS retry patterns, or should we adjust based on production metrics?

---

## Success Criteria

✅ **MsgId-based de-duplication** prevents message-level duplicates  
✅ **Protocol-correct error responses** (admi.002 vs pacs.002)  
✅ **Verification handlers** use INSERT-first pattern  
✅ **Concurrency tests** pass with 100+ concurrent requests  
✅ **Database migration** applied successfully  
✅ **Operational metrics** track all de-duplication paths
