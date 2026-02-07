# SIPS Middleware Data Flow Guide

This guide clarifies the precise message flows for the three core scenarios: Incoming Payments, Incoming Returns, and Outgoing Payments.

## Scenario 1: Incoming Credit Transfer (`pacs.008`)

We receive money from the National Switch (IPS) for a beneficiary in the Core Bank.

1.  **Switch -> Middleware (`pacs.008`)**
    -   Middleware verifies signature.
    -   Persists transaction as `Pending`.
    -   **Active Decision (If Enabled):**
        -   Middleware calls CoreBank (`CB_PaymentRequest` / "Transfer Check").
        -   _If CoreBank Rejects:_ Middleware returns `RJCT` to Switch immediately.
        -   _If CoreBank Accepts (or timeout):_ Middleware returns `ACSC` (Accepted) to Switch.
    -   **Note:** Funds are NOT yet credited locally. The transaction is "Pending Approval from Switch".

2.  **Switch -> Middleware (`pacs.002` - Completion)**
    -   Switch confirms settlement with a `pacs.002` status report.
    -   Middleware verifies signature.
    -   **Finalization - *Conditional***:
        -   **Only if `IncludeCoreBankOnListing = true`**:
            -   Middleware calls `CB_CompletionNotification`.
            -   _Purpose:_ "Transaction finalized. Post funds."
        -   **If `IncludeCoreBankOnListing = false`**:
            -   Middleware calls `CB_PaymentRequest` ("Late Binding").
            -   _Purpose:_ "Here is a new transaction. Process strict credit."
    -   Middleware updates local status to `Success` (or `Failed`).
    -   Middleware returns `ACSC` to Switch.

---

## Scenario 2: Incoming Return (`pacs.004`)

We receive a request (from Switch/Originator) to return funds for a previous successful transaction.

1.  **Switch -> Middleware (`pacs.004`)**
    -   Middleware verifies signature & validates original transaction exists and was `Success`.
    -   **Active Decision (Permission Check) - *Conditional***:
        -   **Only if `IncludeCoreBankOnListing = true`**:
            -   Middleware calls CoreBank (`CB_ReturnRequest`).
            -   _Purpose:_ "Do you agree to return this payment?" (e.g., checks balance).
            -   _If CoreBank Rejects:_ Middleware returns `RJCT` to Switch.
            -   _If CoreBank Accepts:_ Middleware marks transaction as `ReadyForReturn` and returns `ACSC` to Switch.
        -   **If `IncludeCoreBankOnListing = false`**:
            -   Middleware automatically accepts (assuming permission) and marks as `ReadyForReturn`.
            -   Middleware returns `ACSC` to Switch.
    -   **Crucial:** No funds are moved yet. We are waiting for the Switch to confirm the Return lifecycle.

2.  **Switch -> Middleware (`pacs.002` - Confirmation)**
    -   Switch confirms the Return is valid.
    -   Middleware receives `pacs.002` (with `MessageType=ReturnRequest`).
    -   **Execution - *Conditional***:
        -   **Only if `IncludeCoreBankOnListing = true`**:
            -   Middleware calls `CB_CompletionNotification`.
            -   _Purpose:_ "Return finalized. Post reversal."
        -   **If `IncludeCoreBankOnListing = false`**:
            -   Middleware calls `CB_ReturnRequest` ("Late Binding").
            -   _Purpose:_ "Here is a return request. Execute reversal."
    -   CoreBank debits the customer/reverses the credit.
    -   Middleware updates status to `Success` (meaning "Return Successful").

---

## Scenario 3: Outgoing Credit Transfer (`pacs.008`)

Core Bank initiates a payment to an external beneficiary via the Switch.

1.  **CoreBank -> Middleware (API Request)**
    -   CoreBank sends payment data.
    -   Middleware builds `pacs.008`, signs it, and persists as `Pending`.

2.  **Middleware -> Switch (`pacs.008`)**
    -   Middleware sends signed XML to Switch.
    -   Switch responds **Synchronously** with a preliminary `pacs.002`.
        -   _If ACSC:_ Middleware tells CoreBank: "Pending - Accepted by IPS".
        -   _If RJCT:_ Middleware tells CoreBank: "Failed - Rejected by IPS".
    -   **Note:** If `ACSC`, the money is floating. Final confirmation comes later.

3.  **Asynchronous Completion**
    -   **Path A (Standard):** Switch sends a separate `pacs.002` (Completion) later. Middleware updates status to `Success`/`Failed`.
        -   *Note*: CoreBank completion notification is **disabled** by default for outgoing payments (Assumption: Bank already debited fund).
    -   **Path B (SAF/Recovery):** If `pacs.002` is lost, Middleware's SAF (Store-and-Forward) job polls the Switch (`pacs.028`). Upon response, it updates status locally.

---

## Summary of CoreBank Interaction
| Scenario | First Call (Validation/Init) | Second Call (Finalization) |
| :--- | :--- | :--- |
| **Incoming Payment** | `CB_PaymentRequest` (Check/Hold) | `CB_CompletionNotification` (Post) or `CB_PaymentRequest` (Late Binding) |
| **Incoming Return** | `CB_ReturnRequest` (Permission) | `CB_CompletionNotification` (Post) or `CB_ReturnRequest` (Late Binding) |
| **Outgoing Payment** | N/A (CoreBank Initiates) | *Disabled* (Confirmation) |
