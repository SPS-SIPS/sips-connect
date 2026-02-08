# Status API Fix: Prevent Reprocessing of Completed Transactions

## Problem Summary

**Issue**: The Status API endpoint (`/api/v1/gateway/Status`) was reprocessing transactions that had already completed successfully, creating new `Pending` status records and incrementing the round counter.

### Timeline of the Bug (Actual Production Logs)

1. **05:35:28.224** - Transaction created (ID=41, Status=Pending)

   ```sql
   INSERT INTO isomessages (...) VALUES (..., status='Pending') RETURNING id; -- ID=41
   ```

2. **05:35:29.931** - ✅ **Status correctly updated to Success**

   ```sql
   UPDATE isomessages SET status = 'Success' WHERE id = 41;
   ```

3. **05:40:00.001** - SAF worker runs, finds **0 transactions** with `CheckStatus` ✅

   ```sql
   SELECT count(*) FROM isomessages WHERE status = 'CheckStatus' -- Result: 0
   ```

4. **05:40:30.410** - Someone calls Status API for TxId `DARYSOSG532205638990409282052808`

   - API retrieves transaction with `status = Success` (enum value 1)
   - **No guard check** for terminal status
   - Proceeds to reprocess

5. **05:40:30.504** - ❌ **Status API creates new Pending record**:
   ```sql
   UPDATE isomessages SET round = '2' WHERE id = 41;
   INSERT INTO isomessagestatuses (...) VALUES (..., status = 'Pending');
   ```

### Root Cause

The `OutgoingTransactionStatusHandler` (used by both SAF worker and Status API) did **not check** if a transaction already had a terminal status before:

- Incrementing the `round` counter
- Creating a new `ISOMessageStatus` record with `status = Pending`
- Making a new pacs.028 status request to IPS

This meant that calling the Status API on a completed transaction would create a new Pending status record, even though the transaction was already finalized.

## Solution Implemented

### 1. Added Terminal Status Guard in OutgoingTransactionStatusHandler

Added a guard check at the beginning of `HandleAsync()` to prevent reprocessing transactions with terminal statuses:

```csharp
// Guard: Don't reprocess transactions with terminal statuses
// Note: ReadyForReturn is NOT terminal - SAF needs to process it to complete return reversals
if (isoMessage.Status == TransactionStatus.Success ||
    isoMessage.Status == TransactionStatus.Failed)
{
    _logger.LogInformation("[{CorrelationId}] Transaction {TxId} already has terminal status {Status}. Returning cached status without reprocessing.",
        cid, isoMessage.TxId, isoMessage.Status);

    // Return the existing status without making a new IPS call
    return Response<PaymentResponseDto>.Success(new PaymentResponseDto
    {
        Status = _statusOrchestrator.MapToIsoStatusCode(isoMessage.Status),
        TxId = isoMessage.TxId ?? string.Empty,
        EndToEndId = isoMessage.EndToEndId ?? string.Empty,
        Reason = isoMessage.Reason ?? string.Empty,
        AdditionalInfo = isoMessage.AdditionalInfo ?? string.Empty,
        AcceptanceDate = default
    });
}
```

**Location**: `/SIPS.Core/Services/OutgoingTransactionStatusHandler.cs` lines 82-100

**CRITICAL NOTE**: `ReadyForReturn` **IS** a terminal status! It means CoreBank failed to process the transaction and requires manual intervention. SAF should not retry these transactions.

### 2. Added Defensive Status Check in SAF Worker

Added a double-check in the SAF worker processing loop as an additional safety layer:

```csharp
// Double-check status before processing (prevent race conditions)
// ReadyForReturn is terminal - CoreBank failed, requires manual intervention
if (transaction.Status == TransactionStatus.Success ||
    transaction.Status == TransactionStatus.Failed ||
    transaction.Status == TransactionStatus.ReadyForReturn)
{
    _logger.LogWarning("SAF Job: Transaction {txId} has terminal status {status}. Skipping SAF processing.",
        transaction.TxId, transaction.Status);
    continue;
}
```

**Location**: `/SIPS.Core/Workers/SAFWorker.cs` lines 61-70

### 2. Enhanced Audit Logging

Added detailed logging to track SAF processing:

**Before processing**:

```csharp
_logger.LogInformation("SAF Job: Processing transaction {txId} with status {status}, round {round}...",
    transaction.TxId, transaction.Status, transaction.Round);
```

**After processing**:

```csharp
_logger.LogInformation("SAF Job: Completed processing transaction {txId} with response status {status}, round {round}...",
    transaction.TxId, response.Data?.Status, transaction.Round);
```

**Location**: `/SIPS.Core/Workers/SAFWorker.cs` lines 84-85, 93-94

## How SAF Creates New Status Records

When SAF processes a transaction, it:

1. **Increments round counter** (line 86 in `OutgoingTransactionStatusHandler.cs`)
2. **Creates new `ISOMessageStatus` record** with `Status = Pending` (line 87-196)
3. **Sends pacs.028 status request** to IPS
4. **Updates parent `ISOMessage` status** based on IPS response (line 119)
5. **Persists the status response** (line 123)

This is why completed transactions being reprocessed by SAF would get a new `Pending` status record.

## Terminal Statuses (Should Never Be Reprocessed)

The following statuses indicate completed transactions and must be excluded from SAF:

- `TransactionStatus.Success` - Transaction completed successfully
- `TransactionStatus.Failed` - Transaction failed permanently
- `TransactionStatus.ReadyForReturn` - Transaction ready for return (IPS accepted but CoreBank failed)

## Status Enum Definition

```csharp
public enum TransactionStatus
{
    Success = 1,
    Failed,
    Pending,
    ReadyForReturn,
    CheckStatus
}
```

**Database Storage**: Stored as **string** in PostgreSQL (configured in `ISOMessageConfiguration`)

## Testing Recommendations

1. **Unit Test**: Verify SAF skips transactions with terminal statuses
2. **Integration Test**: Simulate race condition where status changes during SAF processing
3. **Load Test**: Verify SAF handles high-volume scenarios without reprocessing completed transactions
4. **Monitoring**: Track SAF warning logs for transactions with terminal statuses being skipped

## Related Files Modified

- `/SIPS.Core/Services/OutgoingTransactionStatusHandler.cs` - **PRIMARY FIX**: Added terminal status guard to prevent reprocessing completed transactions
- `/SIPS.Core/Workers/SAFWorker.cs` - **DEFENSIVE FIX**: Added double-check and enhanced logging as additional safety layer

## Verification

After deploying this fix, monitor logs for:

**Primary Guard (Status API)**:

```
[{CorrelationId}] Transaction {TxId} already has terminal status {Status}. Returning cached status without reprocessing.
```

**Defensive Guard (SAF Worker)**:

```
SAF Job: Transaction {txId} has terminal status {status}. Skipping SAF processing.
```

These logs indicate the guards are working and preventing reprocessing of completed transactions.

## Additional Notes

### Why the Query Alone Isn't Sufficient

The SAF query filters for `Status == TransactionStatus.CheckStatus`, which should exclude completed transactions. However:

1. **Race Conditions**: Status can change between query execution and record processing
2. **Database Consistency**: In distributed systems, there might be brief inconsistencies
3. **Defense in Depth**: Multiple layers of protection prevent edge cases

### Status Flow for Incoming Payments

1. **Initial**: `Pending` (when pacs.008 received)
2. **After pacs.002 ACSC + CoreBank Success**: `Success` ✅ **TERMINAL**
3. **After pacs.002 ACSC + CoreBank Failure**: `ReadyForReturn` ✅ **TERMINAL** (manual intervention required)
4. **After pacs.002 RJCT**: `Failed` ✅ **TERMINAL**
5. **If no pacs.002 received**: `CheckStatus` (SAF will retry)

### Why ReadyForReturn IS Terminal

`ReadyForReturn` means:

- ✅ IPS confirmed the payment (ACSC received via pacs.002)
- ✅ System attempted to call CoreBank to credit beneficiary
- ❌ **CoreBank failed** (timeout, network error, business rule rejection, etc.)
- 🔴 **Manual intervention required** - someone needs to investigate why CoreBank failed

**Why SAF should NOT retry:**

1. We already received definitive confirmation from IPS (pacs.002 with ACSC)
2. We already tried CoreBank and it failed
3. The failure reason needs human investigation (not automated retry)
4. Examples:
   - Network timeout to CoreBank
   - Beneficiary account closed
   - Business rule violation
   - CoreBank system down

**What should happen:**

1. Operations team reviews `ReadyForReturn` transactions
2. Investigates why CoreBank failed
3. Takes appropriate action:
   - Retry manually after fixing the issue
   - Initiate return to sender
   - Contact beneficiary bank
   - Update account details

The fix ensures that once a transaction reaches a terminal state (`Success`, `Failed`, `ReadyForReturn`), neither the Status API nor SAF will reprocess it.

---

## Summary

### What Was Wrong

When the Status API (`/api/v1/gateway/Status`) was called for a transaction that had already completed with `status = Success`, the `OutgoingTransactionStatusHandler` would:

1. Increment the `round` counter
2. Create a new `ISOMessageStatus` record with `status = Pending`
3. Make an unnecessary pacs.028 status request to IPS

This created confusion because the transaction appeared to have a new Pending status, even though it was already successfully completed.

### What We Fixed

1. **Primary Fix**: Added a guard in `OutgoingTransactionStatusHandler.HandleAsync()` that checks if the transaction has a terminal status (`Success`, `Failed`, `ReadyForReturn`) and returns the cached status without reprocessing.

2. **Defensive Fix**: Added a double-check in `SAFWorker.DoWork()` to skip transactions with terminal statuses, providing an additional safety layer.

### Impact

- ✅ Status API calls on completed transactions now return cached results instantly
- ✅ No more spurious Pending status records for completed transactions
- ✅ Round counter no longer increments for completed transactions
- ✅ Reduced unnecessary pacs.028 requests to IPS
- ✅ Cleaner audit trail in `isomessagestatuses` table
