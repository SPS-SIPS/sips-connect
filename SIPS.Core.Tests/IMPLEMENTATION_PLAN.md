# Test Implementation Plan - IncomingPaymentStatusReportHandler

## Status: Ready for Implementation

Due to the large size of the complete test file (~2000+ lines), I will implement it in phases:

### Phase 1: Test Infrastructure (READY)
- ✅ TestBase class with common mocks
- ✅ ISOMessageBuilder for test data
- ✅ TestHelpers for sample messages
- ✅ FluentAssertions integration

### Phase 2: Critical Tests (17 tests total)

#### Happy Path (3 tests) - Lines ~200-400
1. `HandleAsync_WhenAcscAndCoreBankSuccess_ShouldSetStatusToSuccessAndPersist`
2. `HandleAsync_WhenAcscAndCoreBankFails_ShouldSetStatusToReadyForReturn`
3. `HandleAsync_WhenRjctReceived_ShouldSetStatusToFailedAndNotCallCoreBank`

#### Return Completion (3 tests) - Lines ~400-600
4. `HandleAsync_WhenAcscForReadyForReturnTx_ShouldCallCoreBankReturnAndComplete`
5. `HandleAsync_WhenReturnConfirmedAndCbsSuccess_ShouldSetStatusToSuccess`
6. `HandleAsync_WhenReturnConfirmedAndCbsFails_ShouldKeepReadyForReturn`

#### Edge Cases (6 tests) - Lines ~600-1000
7. `HandleAsync_WhenTransactionAlreadySuccess_ShouldReturnAcscAndNotCallCoreBank`
8. `HandleAsync_WhenTransactionAlreadyFailed_ShouldReturnAcscAndNotCallCoreBank`
9. `HandleAsync_WhenTransactionNotFound_ShouldReturnNotFoundResponse`
10. `HandleAsync_WhenCoreBankReturnsNull_ShouldSetStatusToReadyForReturn`
11. `HandleAsync_WhenCoreBankReturnsNullData_ShouldSetStatusToReadyForReturn`
12. `HandleAsync_WhenSignatureInvalid_ShouldReturnAdminMessage`

#### Idempotency (2 tests) - Lines ~1000-1200
13. `HandleAsync_WhenDuplicatePacs002ForSuccessTx_ShouldReturnAcscWithoutReprocessing`
14. `HandleAsync_WhenDuplicatePacs002ForFailedTx_ShouldReturnAcscWithoutReprocessing`

#### StatusOrchestrator Integration (3 tests) - Lines ~1200-1400
15. `HandleAsync_WhenStatusOrchestratorMapsToSuccess_ShouldPersistSuccess`
16. `HandleAsync_WhenStatusOrchestratorMapsToReadyForReturn_ShouldPersistReadyForReturn`
17. `HandleAsync_WhenStatusOrchestratorMapsRjctToFailed_ShouldPersistFailed`

### Implementation Approach

Given the token limit, I recommend:

**Option A: Incremental Implementation**
- Create tests in batches of 5-6
- Verify compilation after each batch
- User confirms before next batch

**Option B: Complete File via Git**
- I provide the complete file structure
- User can copy/paste or I can guide through multi-edit operations

**Option C: Separate Test Files**
- Split into multiple test classes
- `IncomingPaymentStatusReportHandler_HappyPath_Tests.cs`
- `IncomingPaymentStatusReportHandler_ReturnCompletion_Tests.cs`
- etc.

## Recommendation

I recommend **Option A** - implement in 4 batches:
1. Infrastructure + Happy Path (3 tests)
2. Return Completion (3 tests)
3. Edge Cases (6 tests)
4. Idempotency + StatusOrchestrator (5 tests)

This allows for:
- ✅ Verification at each step
- ✅ Immediate feedback
- ✅ Easier debugging
- ✅ Progressive confidence building

**Awaiting your preference on implementation approach.**
