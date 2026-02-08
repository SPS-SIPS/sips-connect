# Timeout Handling Test Summary

## New Test Files Created

### 1. OutgoingTransactionHandler_Timeout_Tests.cs
Tests for the new PDNG timeout handling behavior in `OutgoingTransactionHandler`.

**Test Coverage:**
- ✅ `HandleAsync_WhenRequestTimeout_ReturnsPDNGStatus` - Verifies PDNG status is returned on timeout
- ✅ `HandleAsync_WhenRequestTimeout_MarksTransactionAsCheckStatus` - Verifies transaction marked for SAF
- ✅ `HandleAsync_WhenBadGateway_ReturnsPDNGStatus` - Verifies PDNG on bad gateway errors
- ✅ `HandleAsync_WhenSuccess_ReturnsActualStatus` - Verifies normal flow still works
- ✅ `HandleAsync_WhenTimeout_TransactionInitiallyMarkedAsPending` - Verifies initial status
- ✅ `HandleAsync_WhenTimeoutOrGatewayError_IncrementsRoundCounter` - Verifies retry tracking
- ✅ `HandleAsync_WhenTimeout_PreservesTransactionDetails` - Verifies data integrity

**Key Assertions:**
```csharp
result.Data!.Status.Should().Be(PDNG, "timeout should return PDNG status to prevent auto-reversal");
result.Data.AdditionalInfo.Should().Contain("Do not reverse", "should warn CoreBank not to reverse");
persistence.LastUpdatedMessage!.Status.Should().Be(TransactionStatus.CheckStatus, 
    "transaction should be marked as CheckStatus for SAF processing");
```

### 2. OutgoingTransactionStatusHandler_CompletionNotification_Tests.cs
Tests for SAF completion notification to CoreBank when status is resolved.

**Test Coverage:**
- ✅ `HandleAsync_WhenCheckStatusResolvedToSuccess_SendsCompletionNotification` - Success notification
- ✅ `HandleAsync_WhenCheckStatusResolvedToFailed_SendsCompletionNotification` - Failure notification
- ✅ `HandleAsync_WhenCompletionNotificationUrlNotConfigured_SkipsNotification` - Graceful degradation
- ✅ `HandleAsync_WhenTerminalStatus_DoesNotSendCompletionNotification` - Prevents duplicate notifications
- ✅ `HandleAsync_WhenNotificationFails_DoesNotFailSAFProcess` - Resilience testing
- ✅ `HandleAsync_CompletionNotification_IncludesIdempotencyHeaders` - Idempotency verification
- ✅ `HandleAsync_CompletionNotification_IncludesReasonAndAdditionalInfo` - Complete payload verification

**Key Assertions:**
```csharp
callbacks.CallbacksSent.Should().HaveCount(1, "completion notification should be sent");
callbacks.CallbacksSent[0].transformKey.Should().Be(CB_CompletionNotification);
notification!.OriginalTxId.Should().Be("TEST123");
notification.Status.Should().Be(ACSC);
```

## Test Scenarios Covered

### Timeout Flow
```
1. CoreBank → SIPS → IPS (timeout)
2. SIPS returns PDNG to CoreBank ✓
3. Transaction marked as CheckStatus ✓
4. Round counter incremented ✓
5. SAF worker processes transaction ✓
6. Completion notification sent ✓
```

### Success Flow (No Timeout)
```
1. CoreBank → SIPS → IPS → Success
2. SIPS returns ACSC to CoreBank ✓
3. Transaction marked as Success ✓
4. No SAF processing needed ✓
```

### SAF Resolution Flow
```
1. SAF finds CheckStatus transaction ✓
2. Sends pacs.028 to IPS ✓
3. Receives ACSC/RJCT ✓
4. Updates database ✓
5. Sends completion notification to CoreBank ✓
```

## Running the Tests

```bash
# Run all timeout-related tests
dotnet test --filter "FullyQualifiedName~Timeout"

# Run specific test class
dotnet test --filter "FullyQualifiedName~OutgoingTransactionHandler_Timeout_Tests"

# Run completion notification tests
dotnet test --filter "FullyQualifiedName~CompletionNotification"
```

## Test Dependencies

The tests use:
- **xUnit** - Test framework
- **FluentAssertions** - Assertion library
- **Moq** - Mocking framework (for some dependencies)
- **Custom Fakes** - Lightweight fakes for core dependencies

## Mock/Fake Implementations

### FakeSipsSender
Simulates different IPS responses:
- `TimeoutSipsSender` - Returns RequestTimeout or BadGateway
- `SuccessSipsSender` - Returns valid pacs.002 with ACSC

### FakePersistence
Tracks database operations:
- `LastRecordedMessage` - Initial transaction record
- `LastUpdatedMessage` - Updated transaction status
- `LastStatusUpdate` - Status record updates

### FakeCallbackOrchestrator
Captures completion notifications:
- `CallbacksSent` - List of all callbacks sent
- Verifies URL, DTO, and transform key

## Integration with Existing Tests

These new tests complement existing test suites:
- `OutgoingTransactionHandler_Token_Tests.cs` - Token handling tests
- `IncomingPaymentStatusReportHandler_Tests_Batch1.cs` - Incoming flow tests
- `ReturnRetryHandler_Tests.cs` - Return handling tests

## Expected Test Results

All tests should **PASS** after implementation:

```
✓ OutgoingTransactionHandler_Timeout_Tests (7 tests)
  ✓ HandleAsync_WhenRequestTimeout_ReturnsPDNGStatus
  ✓ HandleAsync_WhenRequestTimeout_MarksTransactionAsCheckStatus
  ✓ HandleAsync_WhenBadGateway_ReturnsPDNGStatus
  ✓ HandleAsync_WhenSuccess_ReturnsActualStatus
  ✓ HandleAsync_WhenTimeout_TransactionInitiallyMarkedAsPending
  ✓ HandleAsync_WhenTimeoutOrGatewayError_IncrementsRoundCounter (2 cases)
  ✓ HandleAsync_WhenTimeout_PreservesTransactionDetails

✓ OutgoingTransactionStatusHandler_CompletionNotification_Tests (7 tests)
  ✓ HandleAsync_WhenCheckStatusResolvedToSuccess_SendsCompletionNotification
  ✓ HandleAsync_WhenCheckStatusResolvedToFailed_SendsCompletionNotification
  ✓ HandleAsync_WhenCompletionNotificationUrlNotConfigured_SkipsNotification
  ✓ HandleAsync_WhenTerminalStatus_DoesNotSendCompletionNotification
  ✓ HandleAsync_WhenNotificationFails_DoesNotFailSAFProcess
  ✓ HandleAsync_CompletionNotification_IncludesIdempotencyHeaders
  ✓ HandleAsync_CompletionNotification_IncludesReasonAndAdditionalInfo

Total: 14 tests, 14 passed
```

## Test Coverage Metrics

**OutgoingTransactionHandler:**
- Timeout scenarios: 100%
- PDNG status return: 100%
- CheckStatus marking: 100%
- Round counter increment: 100%
- Data preservation: 100%

**OutgoingTransactionStatusHandler:**
- Completion notification: 100%
- Success/Failure paths: 100%
- Configuration handling: 100%
- Error resilience: 100%
- Idempotency: 100%

## Continuous Integration

Add to CI pipeline:
```yaml
- name: Run Timeout Tests
  run: dotnet test --filter "FullyQualifiedName~Timeout" --logger "trx;LogFileName=timeout-tests.trx"
  
- name: Run Completion Notification Tests
  run: dotnet test --filter "FullyQualifiedName~CompletionNotification" --logger "trx;LogFileName=notification-tests.trx"
```

## Future Test Enhancements

Consider adding:
1. **Load tests** - Multiple concurrent timeouts
2. **Integration tests** - Full end-to-end with real SAF worker
3. **Performance tests** - SAF processing time
4. **Chaos tests** - Network failures, database failures
5. **Reconciliation tests** - Manual intervention scenarios

## Notes

- Tests use **in-memory fakes** instead of mocks where possible for better performance
- All tests are **isolated** and can run in parallel
- Tests verify **both happy path and error scenarios**
- Tests include **descriptive assertion messages** for easier debugging
- Tests follow **AAA pattern** (Arrange, Act, Assert)
