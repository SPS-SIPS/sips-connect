# Test Coverage Analysis: IncomingPaymentStatusReportHandler

## Step 1: Analysis of Existing Tests

### Current Test File
`/Users/maven/source/SIPS/Packages/SIPS.Core.Tests/Tests/IncomingPaymentStatusReportHandler_Tests.cs`

### Existing Tests (3 total)

1. **`NonPending_DB_Maps_To_ACSC_Without_CB_Call()`** (Lines 61-97)
   - **Quality**: ⚠️ POOR
   - **Issues**:
     - Poor naming (doesn't follow `MethodName_Scenario_ExpectedBehavior` convention)
     - Missing StatusOrchestrator mock (handler now requires it)
     - No FluentAssertions usage
     - Unclear what "NonPending" means (Success? Failed? ReadyForReturn?)
     - No explicit verification of final status
   - **What it tests**: Transaction already in Success status returns ACSC without calling CoreBank

2. **`Pending_DB_Calls_CB_And_Maps_Response()`** (Lines 100-146)
   - **Quality**: ⚠️ POOR
   - **Issues**:
     - Poor naming
     - Missing StatusOrchestrator mock
     - Doesn't test RJCT scenario
     - Doesn't verify transaction status was updated
     - No test for CBS failure scenario
     - Doesn't test the new return completion logic
   - **What it tests**: Pending transaction triggers CoreBank call

3. **`Signature_Failure_Returns_AdminMessage()`** (Lines 149-166)
   - **Quality**: ✅ ACCEPTABLE
   - **Issues**:
     - Could use better assertions
     - Missing StatusOrchestrator mock
   - **What it tests**: Invalid signature returns admin message

### Critical Gaps Identified

❌ **Missing StatusOrchestrator Integration** - All tests fail to mock this critical dependency
❌ **No RJCT (Rejection) Path Tests** - When IPS rejects with RJCT status
❌ **No CBS Failure Tests** - When CoreBank call fails or returns error
❌ **No ReadyForReturn Scenario** - When ACSC + CBS failure → ReadyForReturn
❌ **No Return Completion Tests** - NEW FEATURE: When pacs.002 confirms a return (ReadyForReturn → Success)
❌ **No Idempotency Tests** - Duplicate pacs.002 for already completed transaction
❌ **No Transaction Lookup Failure Tests** - Transaction not found in database
❌ **No Null CoreBank Response Tests** - CBS returns null
❌ **No Status Persistence Verification** - Tests don't verify final status was persisted correctly

---

## Step 2: Gap Analysis and Test Coverage Matrix

| # | Scenario Description | Current Status | Priority | Recommended Test Name |
|---|---------------------|----------------|----------|----------------------|
| **Happy Path Scenarios** |
| 1 | pacs.002 with ACSC, CBS succeeds → Success | Missing | 🔴 CRITICAL | `HandleAsync_WhenAcscAndCoreBankSuccess_ShouldSetStatusToSuccessAndPersist` |
| 2 | pacs.002 with ACSC, CBS fails → ReadyForReturn | Missing | 🔴 CRITICAL | `HandleAsync_WhenAcscAndCoreBankFails_ShouldSetStatusToReadyForReturn` |
| 3 | pacs.002 with RJCT → Failed, no CBS call | Missing | 🔴 CRITICAL | `HandleAsync_WhenRjctReceived_ShouldSetStatusToFailedAndNotCallCoreBank` |
| **Return Completion Scenarios (NEW FEATURE)** |
| 4 | pacs.002 with ACSC for ReadyForReturn tx → CBS Return called | Missing | 🔴 CRITICAL | `HandleAsync_WhenAcscForReadyForReturnTx_ShouldCallCoreBankReturnAndComplete` |
| 5 | pacs.002 with ACSC for ReadyForReturn tx, CBS Return succeeds | Missing | 🔴 CRITICAL | `HandleAsync_WhenReturnConfirmedAndCbsSuccess_ShouldSetStatusToSuccess` |
| 6 | pacs.002 with ACSC for ReadyForReturn tx, CBS Return fails | Missing | 🔴 CRITICAL | `HandleAsync_WhenReturnConfirmedAndCbsFails_ShouldKeepReadyForReturn` |
| **Edge Cases & Error Handling** |
| 7 | Transaction already in Success status → ACSC, no CBS call | Poor | 🟡 HIGH | `HandleAsync_WhenTransactionAlreadySuccess_ShouldReturnAcscAndNotCallCoreBank` |
| 8 | Transaction already in Failed status → ACSC, no CBS call | Missing | 🟡 HIGH | `HandleAsync_WhenTransactionAlreadyFailed_ShouldReturnAcscAndNotCallCoreBank` |
| 9 | Transaction not found in database | Missing | 🟡 HIGH | `HandleAsync_WhenTransactionNotFound_ShouldReturnNotFoundResponse` |
| 10 | CoreBank returns null response | Missing | 🟡 HIGH | `HandleAsync_WhenCoreBankReturnsNull_ShouldSetStatusToReadyForReturn` |
| 11 | CoreBank returns null data | Missing | 🟡 HIGH | `HandleAsync_WhenCoreBankReturnsNullData_ShouldSetStatusToReadyForReturn` |
| 12 | Signature verification fails | Acceptable | 🟢 MEDIUM | `HandleAsync_WhenSignatureInvalid_ShouldReturnAdminMessage` |
| 13 | Parse failure (invalid XML) | Missing | 🟢 MEDIUM | `HandleAsync_WhenParseFailsAndNoFallback_ShouldReturnAdminMessage` |
| **Idempotency & Concurrency** |
| 14 | Duplicate pacs.002 for already Success tx | Missing | 🟡 HIGH | `HandleAsync_WhenDuplicatePacs002ForSuccessTx_ShouldReturnAcscWithoutReprocessing` |
| 15 | Duplicate pacs.002 for already Failed tx | Missing | 🟢 MEDIUM | `HandleAsync_WhenDuplicatePacs002ForFailedTx_ShouldReturnAcscWithoutReprocessing` |
| **StatusOrchestrator Integration** |
| 16 | StatusOrchestrator maps ACSC + CBS Success → Success | Missing | 🔴 CRITICAL | `HandleAsync_WhenStatusOrchestratorMapsToSuccess_ShouldPersistSuccess` |
| 17 | StatusOrchestrator maps ACSC + CBS Failure → ReadyForReturn | Missing | 🔴 CRITICAL | `HandleAsync_WhenStatusOrchestratorMapsToReadyForReturn_ShouldPersistReadyForReturn` |
| 18 | StatusOrchestrator maps RJCT → Failed | Missing | 🔴 CRITICAL | `HandleAsync_WhenStatusOrchestratorMapsRjctToFailed_ShouldPersistFailed` |

### Summary Statistics

- **Total Scenarios**: 18
- **Currently Tested**: 3 (17%)
- **Missing**: 14 (78%)
- **Poor Quality**: 1 (5%)
- **Critical Priority**: 9 scenarios
- **High Priority**: 6 scenarios
- **Medium Priority**: 3 scenarios

### Coverage by Category

| Category | Total | Tested | Missing | Coverage % |
|----------|-------|--------|---------|------------|
| Happy Path | 3 | 0 | 3 | 0% |
| Return Completion (NEW) | 3 | 0 | 3 | 0% |
| Edge Cases | 7 | 1 | 6 | 14% |
| Idempotency | 2 | 0 | 2 | 0% |
| StatusOrchestrator | 3 | 0 | 3 | 0% |
| **TOTAL** | **18** | **1** | **17** | **6%** |

---

## Step 3: Before/After Refactoring Example

### Selected Test for Refactoring
`NonPending_DB_Maps_To_ACSC_Without_CB_Call()` → Refactor to `HandleAsync_WhenTransactionAlreadySuccess_ShouldReturnAcscAndNotCallCoreBank()`

### BEFORE (Current Poor Implementation)

```csharp
[Fact]
public async Task NonPending_DB_Maps_To_ACSC_Without_CB_Call()
{
    var sig = new Mock<ISignatureService>();
    sig.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
       .ReturnsAsync((true, "ok"));

    var iso = new ISOMessage
    {
        Status = TransactionStatus.Success,
        FromBIC = "FROM",
        ToBIC = "TO",
        TxId = "T1",
        EndToEndId = "E1",
        BizMsgIdr = "B",
        MsgDefIdr = "pacs.008.001.10",
        MsgId = "M"
    };
    var pg = new Mock<IPersistenceGateway>();
    pg.Setup(p => p.GetISOMessageByTxIdAsync("T1", It.IsAny<CancellationToken>())).ReturnsAsync(iso);
    pg.Setup(p => p.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new ISOMessageStatus { ISOMessage = iso });

    var jsonAdapter = new Mock<IJsonAdapter>();
    var callback = new Mock<ICallbackClient>(MockBehavior.Strict);
    var signer = new Mock<INativeSigner>();
    signer.Setup(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>()))
          .Returns<string, string>((m, alg) => m);

    var handler = BuildHandler(sig.Object, pg.Object, jsonAdapter.Object, callback.Object, signer.Object);

    var xml = SamplePacs002WithTxId("T1");
    var rsp = await handler.HandleAsync(xml, CancellationToken.None);

    Assert.Contains("T1", rsp);
    Assert.Contains("ACSC", rsp);
    callback.Verify(c => c.SendAsync(It.IsAny<string>(), It.IsAny<System.Collections.Generic.Dictionary<string, string>>(), It.IsAny<System.Net.Http.StringContent>(), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Never);
}
```

**Problems:**
1. ❌ Poor test name doesn't describe scenario
2. ❌ Missing StatusOrchestrator mock (will fail in current code)
3. ❌ No FluentAssertions
4. ❌ No AAA pattern separation
5. ❌ Unclear what "NonPending" means
6. ❌ No explicit verification of transaction status
7. ❌ Uses string.Contains instead of structured assertions

### AFTER (Refactored to Best Practices)

```csharp
[Fact]
public async Task HandleAsync_WhenTransactionAlreadySuccess_ShouldReturnAcscAndNotCallCoreBank()
{
    // Arrange
    const string txId = "TX-SUCCESS-001";
    const string expectedBic = "TESTBIC001";
    
    var mockSignatureService = new Mock<ISignatureService>();
    mockSignatureService
        .Setup(x => x.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((true, "Signature valid"));

    var existingTransaction = new ISOMessage
    {
        Status = TransactionStatus.Success, // Already completed
        FromBIC = expectedBic,
        ToBIC = "RECEIVERBIC",
        TxId = txId,
        EndToEndId = "E2E-001",
        BizMsgIdr = "BIZ-MSG-001",
        MsgDefIdr = "pacs.008.001.10",
        MsgId = "MSG-001",
        Transactions = new List<Transaction>()
    };

    var mockPersistence = new Mock<IPersistenceGateway>();
    mockPersistence
        .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(existingTransaction);
    
    mockPersistence
        .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ISOMessageStatus { ISOMessage = existingTransaction });

    var mockCallbackClient = new Mock<ICallbackClient>(MockBehavior.Strict);
    var mockJsonAdapter = new Mock<IJsonAdapter>();
    var mockStatusOrchestrator = new Mock<IStatusOrchestrator>();
    
    var mockSigner = new Mock<INativeSigner>();
    mockSigner
        .Setup(x => x.SignEnvelope(It.IsAny<string>(), It.IsAny<string>()))
        .Returns<string, string>((message, _) => message);

    var handler = BuildHandlerWithMocks(
        mockSignatureService.Object,
        mockPersistence.Object,
        mockJsonAdapter.Object,
        mockCallbackClient.Object,
        mockSigner.Object,
        mockStatusOrchestrator.Object);

    var pacs002Message = CreateSamplePacs002(txId, "ACSC");

    // Act
    var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

    // Assert
    result.Should().NotBeNullOrEmpty();
    result.Should().Contain(txId);
    result.Should().Contain("ACSC");
    
    // Verify CoreBank was NOT called (transaction already complete)
    mockCallbackClient.Verify(
        x => x.SendAsync(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<StringContent>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<string>()),
        Times.Never,
        "CoreBank should not be called for already completed transactions");
    
    // Verify transaction status was not changed
    existingTransaction.Status.Should().Be(TransactionStatus.Success);
}
```

**Improvements:**
1. ✅ Clear, descriptive name following convention
2. ✅ AAA pattern with clear sections
3. ✅ FluentAssertions for readable assertions
4. ✅ StatusOrchestrator mock included
5. ✅ Explicit verification with descriptive messages
6. ✅ Constants for magic strings
7. ✅ Comprehensive assertions
8. ✅ MockBehavior.Strict for callback ensures no unexpected calls

---

## Step 4: Implementation of New Tests (Coming Next)

The following sections will contain the complete implementation of all 17 missing tests, organized by category:

1. **Happy Path Tests** (3 tests)
2. **Return Completion Tests** (3 tests)
3. **Edge Cases & Error Handling** (6 tests)
4. **Idempotency Tests** (2 tests)
5. **StatusOrchestrator Integration Tests** (3 tests)

Each test will follow the refactored pattern demonstrated above.

---

## Recommendations

### Immediate Actions

1. ✅ Add `IStatusOrchestrator` mock to test base/builder
2. ✅ Refactor all 3 existing tests to follow best practices
3. ✅ Implement all 17 missing tests
4. ✅ Add FluentAssertions NuGet package if not present
5. ✅ Create test data builders for common scenarios

### Test Infrastructure Improvements

1. **Create TestBase Class**
   ```csharp
   public abstract class IncomingPaymentStatusReportHandlerTestBase
   {
       protected Mock<ILogger<IncomingPaymentStatusReportHandler>> MockLogger;
       protected Mock<ISignatureService> MockSignatureService;
       protected Mock<IPersistenceGateway> MockPersistence;
       protected Mock<IStatusOrchestrator> MockStatusOrchestrator;
       // ... other common mocks
       
       protected IncomingPaymentStatusReportHandlerTestBase()
       {
           // Initialize common mocks
       }
   }
   ```

2. **Create Test Data Builders**
   ```csharp
   public class ISOMessageBuilder
   {
       public static ISOMessage CreatePendingTransaction(string txId) { ... }
       public static ISOMessage CreateSuccessTransaction(string txId) { ... }
       public static ISOMessage CreateReadyForReturnTransaction(string txId) { ... }
   }
   ```

3. **Create Helper Methods**
   ```csharp
   private static string CreateSamplePacs002(string txId, string status) { ... }
   private static CBPaymentStatusResponseDto CreateCbsSuccessResponse(string txId) { ... }
   private static CBPaymentStatusResponseDto CreateCbsFailureResponse(string txId) { ... }
   ```

---

## Next Steps

1. Await confirmation to proceed with full test implementation
2. Implement all 17 missing tests
3. Refactor 2 remaining poor tests
4. Add test infrastructure (TestBase, Builders, Helpers)
5. Verify all tests pass
6. Move to next handler (IncomingTransactionHandler)
