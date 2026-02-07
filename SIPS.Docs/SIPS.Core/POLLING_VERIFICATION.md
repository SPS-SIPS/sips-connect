# Creditor Status Polling Verification

## Overview
Implemented automatic status polling for "Creditor" transactions (Incoming Payments) that are stuck in `Pending` state.

## Components Implemented

### 1. Configuration (`CoreOptions.cs`)
- Added `TransactionTimeoutMinutes`: Default 60 minutes.
- Added `TimeoutWorkerSchedule`: Default `*/15 * * * *` (every 15 minutes).

### 2. Timeout Worker (`TimeoutWorker.cs`)
- **Action**: Queries `ISOMessages` for transactions with `Status == Pending` and `Date <= Now - Timeout`.
- **Logic**: Updates status to `CheckStatus` and resets `Round` to 0.
- **Schedule**: configurable via `TimeoutWorkerSchedule`.
- **Registration**: Added to `DI.cs`.

### 3. SAF Worker (`SAFWorker.cs`)
- **Update**: Modified query to include Incoming Payments (`ToBIC == Us`) in the `CheckStatus` processing loop.
- **Previous**: `(Status == CheckStatus && FromBIC == Us) || ReadyForReturn`
- **Current**: `(Status == CheckStatus && (FromBIC == Us || ToBIC == Us)) || ReadyForReturn`

### 4. Status Check Logic (`OutgoingTransactionStatusHandler.cs`)
- **Issue Identified**: `BuildRequest` was hardcoded to set `To = isoMessage.FromBIC`.
    - Correct for Incoming Payments (`From` = Sender).
    - **Incorrect** for Outgoing Payments (`From` = Us).
- **Fix Applied**: Dynamic `To` assignment:
    ```csharp
    To = isoMessage.FromBIC == fromBIC ? isoMessage.ToBIC : isoMessage.FromBIC
    ```
    - Incoming (`From`!=Us): Send to `From` (Sender).
    - Outgoing (`From`==Us): Send to `To` (Receiver).
- **Notification**: Existing logic notifies CoreBank upon `Success`/`Failed` completion, which applies to these recovered Incoming Payments.

## Verification Scenarios

### Scenario A: Incoming Payment Stuck in Pending
1.  **State**: Incoming Payment (`pacs.008`) arrives. Persisted as `Pending`.
2.  **Failure**: Middleware fails to receive `pacs.002` confirmation to move status forward.
    -   **Strict Mode (`IncludeCoreBankOnListing = false`)**: CoreBank has NOT received anything yet (Late Binding).
    -   **Permissive Mode (`IncludeCoreBankOnListing = true`)**: CoreBank received initial request and is holding it as `Pending`.
3.  **Timeout**: 60 minutes pass.
4.  **TimeoutWorker**: Runs. Finds transaction. Updates status to `CheckStatus`.
5.  **SAFWorker**: Runs. Picks up transaction (now `CheckStatus`).
6.  **Status Check**: `OutgoingTransactionStatusHandler` sends `pacs.028` to Sender (`FromBIC`).
7.  **Response**: Sender replies with `ACSC` (Accepted/Success) or `RJCT`.
8.  **Completion**: Handler updates status.
9.  **Notification/Transfer**:
    -   **Strict Mode**: Calls `CB_PaymentRequest` (Transfer) to execute Late Binding (Money In).
    -   **Permissive Mode**: Calls `CB_CompletionNotification` to finalize Pending transaction.

### Scenario B: Outgoing Payment Stuck in Pending
1.  **State**: Outgoing Payment sent. `Pending`.
2.  **Failure**: No `pacs.002` ever received.
3.  **Timeout**: 60 minutes pass.
4.  **TimeoutWorker**: Moves to `CheckStatus`.
5.  **SAFWorker**: Picks it up.
6.  **Status Check**: Sends `pacs.028` to Receiver (`ToBIC`) - **Fixed logic ensures this goes to Receiver, not Us.**
7.  **Completion**: resolved.

## Conclusion
The implementation covers the requirements for Creditor Status Polling and also robustifies the Outgoing Status Polling logic.

## Reference: Test Results
```bash
Test summary: total: 91, failed: 0, succeeded: 84, skipped: 7, duration: 2.8s
Build succeeded in 5.4s
```
