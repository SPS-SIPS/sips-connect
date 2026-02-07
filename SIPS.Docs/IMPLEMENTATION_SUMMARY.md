# Timeout Handling Implementation Summary

## Overview
Implemented Option 1 solution to prevent double-payment scenarios when SIPS times out waiting for IPS response but the transaction was successfully processed.

## Changes Made

### 1. Core Implementation

#### `/SIPS.Core/Constants.cs`
- ✅ Added `PDNG` constant for pending status code

#### `/SIPS.Core/Services/OutgoingTransactionHandler.cs`
- ✅ Modified timeout handling to return `PDNG` status instead of failure
- ✅ Added detailed warning message to prevent CoreBank auto-reversal
- ✅ Transaction marked as `CheckStatus` for SAF processing
- ✅ Round counter incremented for retry tracking

**Key Change:**
```csharp
// OLD: return Response<PaymentResponseDto>.Fail("Request to IPS timed out...", timeout);
// NEW: return Response<PaymentResponseDto>.Success(new PaymentResponseDto { Status = PDNG, ... });
```

#### `/SIPS.Core/Services/OutgoingTransactionStatusHandler.cs`
- ✅ Added `NotifyCoreBankCompletionAsync` method
- ✅ SAF worker now sends completion notification when status is resolved
- ✅ Notifications sent for both Success and Failed final statuses
- ✅ Graceful handling when notification URL not configured
- ✅ Error resilience - SAF continues even if notification fails

### 2. Documentation

#### `/COREBANK_TIMEOUT_INTEGRATION.md`
Comprehensive integration guide for CoreBank team including:
- ✅ Problem statement and solution overview
- ✅ Required CoreBank changes (PDNG handling, completion endpoint)
- ✅ API contracts and request/response formats
- ✅ Transaction flow diagrams
- ✅ Error scenarios and handling
- ✅ Testing checklist
- ✅ Reconciliation process
- ✅ Security considerations
- ✅ Migration plan

### 3. Test Coverage

#### `/SIPS.Core.Tests/Tests/OutgoingTransactionHandler_Timeout_Tests.cs`
**7 comprehensive tests:**
- ✅ PDNG status returned on RequestTimeout
- ✅ PDNG status returned on BadGateway
- ✅ Transaction marked as CheckStatus
- ✅ Round counter incremented
- ✅ Transaction details preserved
- ✅ Normal success flow still works
- ✅ Initial Pending status verified

#### `/SIPS.Core.Tests/Tests/OutgoingTransactionStatusHandler_CompletionNotification_Tests.cs`
**7 comprehensive tests:**
- ✅ Completion notification sent on success resolution
- ✅ Completion notification sent on failure resolution
- ✅ Graceful handling when URL not configured
- ✅ No duplicate notifications for terminal statuses
- ✅ SAF resilience when notification fails
- ✅ Idempotency headers included
- ✅ Complete payload verification

#### `/SIPS.Core.Tests/TIMEOUT_HANDLING_TEST_SUMMARY.md`
- ✅ Test documentation and coverage metrics
- ✅ Running instructions
- ✅ Expected results

## Transaction Flow

### Before (Problematic)
```
CoreBank → SIPS → IPS (✓ Success)
         ← FAIL ← (timeout)
         ↓
    Auto-Reverse ❌ DOUBLE PAYMENT!
```

### After (Safe)
```
CoreBank → SIPS → IPS (✓ Success)
         ← PDNG ← (timeout)
         ↓
    Mark Pending ✓ (Wait for SAF)
         
         [SAF Worker checks status]
         
         ← Completion Notification (ACSC) ←
         ↓
    Finalize Success ✓
```

## CoreBank Integration Requirements

### Must Implement:

1. **PDNG Status Handling**
   - Accept `PDNG` as valid response status
   - Do NOT auto-reverse on PDNG
   - Keep transaction in pending state

2. **Completion Notification Endpoint**
   - URL: Configure in `ISO20022Options.CompletionNotification`
   - Accept POST requests with `CBCompletionNotification` payload
   - Handle idempotent retries
   - Update transaction based on final status

3. **Configuration**
   ```json
   {
     "ISO20022Options": {
       "CompletionNotification": "https://corebank.example.com/api/sips/completion-notification"
     }
   }
   ```

## SAF Configuration

Existing SAF worker automatically handles timeout resolution:
- `SAFExpression`: Cron schedule (e.g., "*/5 * * * *")
- `SAFMaxRetries`: Maximum attempts (default: 10)
- `SAFPage`: Batch size (default: 40)

## Testing

### Run Tests
```bash
# All timeout tests
dotnet test --filter "FullyQualifiedName~Timeout"

# Completion notification tests
dotnet test --filter "FullyQualifiedName~CompletionNotification"
```

### Expected Results
- 14 new tests, all passing
- 100% coverage of timeout scenarios
- 100% coverage of completion notification

## Deployment Checklist

### SIPS Side (Completed)
- [x] Add PDNG constant
- [x] Modify timeout handling
- [x] Add completion notification
- [x] Write tests
- [x] Document integration requirements

### CoreBank Side (Required)
- [ ] Implement PDNG status handling
- [ ] Create completion notification endpoint
- [ ] Configure notification URL
- [ ] Update transaction state machine
- [ ] Test timeout scenarios
- [ ] Test completion notifications
- [ ] Deploy to staging
- [ ] Deploy to production

## Monitoring

### SIPS Metrics to Monitor:
- Count of transactions with `PDNG` status
- Count of transactions in `CheckStatus` 
- SAF processing time
- Completion notification success rate
- Completion notification failure rate

### CoreBank Metrics to Monitor:
- Count of transactions in pending state
- Time in pending state (alert if > SAF interval)
- Completion notification receipt rate
- Manual reconciliation count

## Risk Mitigation

### What if completion notification fails?
- Transaction status still updated in SIPS database
- CoreBank can query SIPS status endpoint
- Manual reconciliation process available
- Notification retries with idempotency

### What if SAF never resolves?
- After `SAFMaxRetries` (default 10), marked as Failed
- Completion notification sent with RJCT
- CoreBank can reverse the debit
- Manual investigation for stuck transactions

### What if IPS is down?
- SAF continues retrying
- Transaction stays in CheckStatus
- Alerts triggered after threshold
- Manual intervention if needed

## Rollback Plan

If issues arise:
1. Revert `OutgoingTransactionHandler.cs` timeout handling
2. Return to failure response (old behavior)
3. CoreBank continues auto-reversal (accepts risk)
4. Investigate and fix issues
5. Re-deploy with fixes

## Success Criteria

✅ No double-payments due to timeout scenarios
✅ CoreBank receives PDNG status on timeout
✅ SAF resolves transaction status
✅ CoreBank receives completion notification
✅ All tests passing
✅ Zero production incidents

## Next Steps

1. **Share documentation** with CoreBank team
2. **Schedule integration meeting** to review requirements
3. **CoreBank implements** PDNG handling and completion endpoint
4. **Integration testing** in staging environment
5. **Load testing** timeout scenarios
6. **Production deployment** with monitoring
7. **Post-deployment review** after 1 week

## Files Modified/Created

### Modified:
- `/SIPS.Core/Constants.cs`
- `/SIPS.Core/Services/OutgoingTransactionHandler.cs`
- `/SIPS.Core/Services/OutgoingTransactionStatusHandler.cs`

### Created:
- `/COREBANK_TIMEOUT_INTEGRATION.md`
- `/SIPS.Core.Tests/Tests/OutgoingTransactionHandler_Timeout_Tests.cs`
- `/SIPS.Core.Tests/Tests/OutgoingTransactionStatusHandler_CompletionNotification_Tests.cs`
- `/SIPS.Core.Tests/TIMEOUT_HANDLING_TEST_SUMMARY.md`
- `/IMPLEMENTATION_SUMMARY.md` (this file)

## Contact

For questions or issues:
- **SIPS Implementation**: Review code changes in this PR
- **CoreBank Integration**: See `COREBANK_TIMEOUT_INTEGRATION.md`
- **Testing**: See `TIMEOUT_HANDLING_TEST_SUMMARY.md`

---

**Implementation Date**: November 23, 2025
**Status**: ✅ Complete - Ready for CoreBank Integration
