# Investigation Results

## Key Finding: Handler Code is Correct, Tests Need Adjustment

### StatusOrchestrator Implementation (CORRECT)

The `StatusOrchestrator.MapCompletionStatus` implementation is **correct** and matches the architectural design:

```csharp
// Rule 1: IPS RJCT → Failed
if (ipsCode == RJCT)
    return (Failed, Failed, "Received rejection confirmation", ...);

// Rule 2: IPS ACSC/SUCC
if (ipsCode == ACSC || ipsCode == SUCC)
{
    // Rule 2a: No CoreBank response → ReadyForReturn
    if (string.IsNullOrWhiteSpace(cbCode))
        return (ReadyForReturn, ReadyForReturn, "CoreBank callback failed", ...);
    
    // Rule 2b: CoreBank success → Success
    if (cbCode == ACSC || cbCode == SUCC)
        return (Success, Success, "Processed Transaction", ...);
    
    // Rule 2c: CoreBank rejection → ReadyForReturn
    return (ReadyForReturn, ReadyForReturn, "Transaction is ready for return", ...);
}

// Rule 3: Unknown → Failed
return (Failed, Failed, "Unknown status from IPS", ...);
```

### Handler Flow (CORRECT)

1. **Non-Pending transactions** (line 208-234): Early return, status unchanged ✅
2. **RJCT from IPS** (line 237-253): Uses StatusOrchestrator, sets Failed ✅
3. **ReadyForReturn transactions** (line 257-270): Calls CB Return endpoint ✅
4. **Normal flow** (line 316+): Calls CB Transfer, uses StatusOrchestrator ✅

### The Real Problem

The tests are setting up scenarios correctly, but the **default mock in TestBase** is interfering with specific test setups. Moq matches setups in order, and more specific setups should override general ones, but our default mock is too broad.

## Solution

Remove the default StatusOrchestrator mock from TestBase and let each test set up exactly what it needs. This way:
- Tests that need StatusOrchestrator will set it up explicitly
- Tests that don't need it (early returns) won't be affected
- No interference between default and specific mocks

## Action Plan

1. Remove default StatusOrchestrator mock from TestBase
2. Add explicit StatusOrchestrator setups to each test that needs it
3. Tests for non-Pending transactions won't need StatusOrchestrator (early return)
4. All tests should pass after this change
