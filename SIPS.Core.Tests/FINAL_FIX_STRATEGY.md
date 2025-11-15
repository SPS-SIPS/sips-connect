# Final Fix Strategy

## Root Cause Identified

The tests are failing because:

1. ✅ **Using REAL StatusOrchestrator** - This is correct!
2. ❌ **CB Status Not Being Parsed** - The `crResponse?.Status` is coming back empty/null
3. ❌ **Test Expectations Don't Match Reality** - Tests expect specific text that doesn't match StatusOrchestrator

## The Real Problem

When the handler calls `ParseCallbackResult(result.Data)`, it needs to:
1. Transform the JsonObject using `_jsonAdapter.Transform(js!, "CB_PaymentResponse")`
2. Convert to DTO using `_jsonAdapter.ToObject<CBPaymentStatusResponseDto>(md)`
3. Map to `PaymentResponseDto`

Our mocks are set up, but the `PaymentResponseDto` that comes out has an empty `Status` field!

Looking at handler line 358:
```csharp
var crResponse = result.Data != null ? ParseCallbackResult(result.Data) : new PaymentResponseDto { Status = string.Empty, TxId = string.Empty };
```

The `ParseCallbackResult` method returns a `PaymentResponseDto` with the Status field populated from `CBPaymentStatusResponseDto.Status`.

**The issue:** The mock setup for `ToObject` returns a `CBPaymentStatusResponseDto`, but `ParseCallbackResult` needs to map it to `PaymentResponseDto`. Let me check if there's a mapping issue.

Actually, looking at the handler code more carefully, `ParseCallbackResult` creates a NEW `PaymentResponseDto` and copies fields from `CBPaymentStatusResponseDto`:

```csharp
return new PaymentResponseDto
{
    Status = cb.Status ?? string.Empty,
    TxId = cb.TxId ?? string.Empty,
    AcceptanceDate = cb.AcceptanceDate,
    AdditionalInfo = cb.AdditionalInfo,
    Reason = cb.Reason,
    EndToEndId = cb.EndToEndId ?? string.Empty
};
```

So if `cb` is null (because `ToObject` returns null), then Status will be empty string.

## Solution

The mock setup `MockJsonAdapter.ToObject<CBPaymentStatusResponseDto>` needs to return a non-null DTO with Status = "ACSC".

Let me verify our current setup is correct and if not, fix it.

Actually, looking at line 257:
```csharp
MockJsonAdapter
    .Setup(x => x.ToObject<CBPaymentStatusResponseDto>(It.IsAny<JsonObject>()))
    .Returns(TestHelpers.CreateCbsSuccessResponse(txId));
```

This SHOULD return a DTO with Status = "ACSC". So why is it not working?

**AH! The issue might be that `ParseCallbackResult` checks if `cb == null` and returns a default empty DTO!**

Let me check the handler's `ParseCallbackResult` implementation to see if there's a null check that's failing.

## Action Plan

1. Add logging/debugging to understand why Status is empty
2. Fix the mock setup if needed
3. Update test expectations to match REAL StatusOrchestrator strings
4. Remove all StatusOrchestrator mock setups from individual tests (they're not needed with real implementation)
