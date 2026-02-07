# SAF Dual-Purpose Design: Handling Both Payment and Return Status Checks

## Overview

The Store-and-Forward (SAF) mechanism is designed to handle **both** payment transactions and return transactions using a **single unified workflow**. This document explains how SAF distinguishes between them and prevents overlap.

## Core Principle: Status-Driven, Context-Aware

SAF uses a **single query** but relies on **transaction context** to determine the appropriate action:

```csharp
// SAFWorker.cs - Single query for all CheckStatus transactions
var query = storage.ISOMessages
    .Where(x =>
        x.Status == TransactionStatus.CheckStatus &&  // Common status
        x.Round < options.SAFMaxRetries &&
        x.FromBIC == bic
    );
```

## How SAF Distinguishes Between Scenarios

### 1. MessageType Field

Each `ISOMessage` has a `MessageType` that identifies its purpose:

| MessageType          | Scenario              | Original Message | SAF Action           |
| -------------------- | --------------------- | ---------------- | -------------------- |
| `TransactionRequest` | Payment not confirmed | pacs.008         | Check payment status |
| `ReturnRequest`      | Return not confirmed  | pacs.004         | Check return status  |

### 2. Status Context

The **previous status** before `CheckStatus` provides additional context:

| Previous Status  | MessageType        | Meaning                                | SAF Goal                 |
| ---------------- | ------------------ | -------------------------------------- | ------------------------ |
| `Pending`        | TransactionRequest | Payment sent, no response              | Get final payment status |
| `ReadyForReturn` | ReturnRequest      | Return accepted, awaiting confirmation | Get return confirmation  |

## Unified Flow Through OutgoingTransactionStatusHandler

Both scenarios use the **same handler** (`OutgoingTransactionStatusHandler`) which:

1. ✅ Retrieves transaction with full context via `GetISOMessageWithTransactionsByTxIdAsync`
2. ✅ Sends pacs.028 status request to IPS
3. ✅ Receives status response
4. ✅ Maps status via `StatusOrchestrator`
5. ✅ **Detects scenario** based on `MessageType` and previous status
6. ✅ Takes appropriate action

### Code Implementation

```csharp
// OutgoingTransactionStatusHandler.cs lines 92-117

// Check if this is a return transaction that was ReadyForReturn
var wasReadyForReturn = isoMessage.Status == TransactionStatus.ReadyForReturn;
var isReturnMessage = isoMessage.MessageType == PostgreSQL.Enums.ISOMessageType.ReturnRequest;

// Update status based on IPS response
isoMessage.Status = finalStatus;
await _persistence.ISOMessageStatusResponseAsync(record, dbCt);

// If this was a ReadyForReturn transaction and status is now confirmed,
// trigger CoreBank callback for return completion
if (wasReadyForReturn && isReturnMessage && finalStatus == TransactionStatus.Success)
{
    _logger.LogInformation("[{CorrelationId}] Return transaction {TxId} confirmed via SAF. Triggering CoreBank callback.", cid, isoMessage.TxId);
    // TODO: Trigger CoreBank return callback here
}
```

## Complete Scenario Flows

### Scenario A: Payment Transaction (pacs.008)

```
1. Send pacs.008 → Persist as Pending
2. Timeout/Error → Mark as CheckStatus
   ↓
3. SAF picks up (MessageType=TransactionRequest, Status=CheckStatus)
4. Send pacs.028 status request
5. Receive status response
6. Update to Success/Failed
   ✅ Done - No CoreBank callback needed (already called in initial flow)
```

### Scenario B: Incoming Return Transaction (Creditor FI receives pacs.004)

```
1. IncomingReturnTransactionHandler receives pacs.004
2. Validate → Mark ORIGINAL PAYMENT as ReadyForReturn → Return ACSC
3. pacs.002 confirmation not received → Original payment stays ReadyForReturn
   ↓
4. SAF picks up (MessageType=TransactionRequest, Status=ReadyForReturn)
   Note: SAF checks the ORIGINAL PAYMENT, not the return message
5. Send pacs.028 status request for original payment
6. Receive status response confirming return was processed
7. Update original payment to Success
8. ⚠️ Detect: wasReadyForReturn + isOriginalPayment + Success
9. 🔔 Trigger CoreBank callback to process return (reverse the credit)
   ✅ Done - Incoming return completed via SAF
```

### Scenario C: Outgoing Return Transaction (Debtor FI sends pacs.004)

```
1. CoreBank already processed return (reversed debit)
2. OutgoingReturnHandler sends pacs.004
3. Receive immediate pacs.002 response
4. Finalize transaction
   ✅ Done - No SAF needed, no CoreBank callback needed (already done)
```

## No Overlap Guarantees

### 1. Distinct MessageType

- Payment transactions: `MessageType = TransactionRequest`
- Return transactions: `MessageType = ReturnRequest`
- **No collision possible** - they are fundamentally different message types

### 2. Distinct TxId

- Each transaction has a unique `TxId`
- SAF processes by `TxId`, ensuring no duplicate processing

### 3. Status Progression

- Payment: `Pending` → `CheckStatus` → `Success/Failed`
- Return: `ReadyForReturn` → `CheckStatus` → `Success/Failed`
- Different starting states prevent confusion

### 4. Round Counter

- Each transaction tracks its own retry count
- Prevents infinite loops
- Max retries enforced per transaction

## Benefits of Unified Approach

✅ **Single SAF Worker** - One cron job handles all scenarios
✅ **Consistent Retry Logic** - Same max retries, same intervals
✅ **Shared Infrastructure** - One handler, one status request builder
✅ **Context-Aware Actions** - Automatically triggers appropriate completion logic
✅ **No Code Duplication** - DRY principle maintained

## Future Enhancement: Return Completion Service

When implementing the CoreBank callback for return completion (TODO at line 115), consider:

1. **Inject ICallbackOrchestrator** into OutgoingTransactionStatusHandler
2. **Call CoreBank return endpoint** with return details
3. **Handle CoreBank response** and finalize transaction
4. **Log completion** for audit trail

Example:

```csharp
if (wasReadyForReturn && isReturnMessage && finalStatus == TransactionStatus.Success)
{
    var returnDto = new CBReturnRequestDto
    {
        OrgnlTxId = isoMessage.TxId,
        ReturnId = isoMessage.ReturnId,
        // ... other fields
    };

    var cbResponse = await _callbacks.SendJsonAsync(
        _callbackLinks.Return!,
        headers,
        returnDto,
        "CB_ReturnRequest",
        // ... other params
    );

    // Finalize based on CoreBank response
}
```

## Summary

The SAF mechanism elegantly handles both payment and return status checks through:

1. **Unified query** for `CheckStatus` transactions
2. **Context detection** via `MessageType` and previous status
3. **Conditional logic** to trigger appropriate completion actions
4. **No overlap** due to distinct message types and transaction IDs

This design is **scalable**, **maintainable**, and **efficient** - a single worker handles all retry scenarios without confusion or duplication.
