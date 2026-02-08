# SIPS Handler Test Scenarios

This document provides detailed test scenarios for each handler with expected behaviors and validation points.

## Table of Contents
1. [IncomingTransactionHandler](#incomingtransactionhandler)
2. [IncomingVerificationHandler](#incomingverificationhandler)
3. [IncomingTransactionStatusHandler](#incomingtransactionstatushandler)
4. [IncomingPaymentStatusReportHandler](#incomingpaymentstatusreporthandler)

---

## IncomingTransactionHandler

**Purpose:** Processes incoming pacs.008 payment requests

### Test Scenario 1: Successful Payment Request

**Payload:** `Payloads/pacs.008.xml`

**Expected Behavior:**
1. Handler validates XML signature
2. Parses pacs.008 message
3. Records incoming transaction in database
4. Calls CoreBank payment endpoint
5. Returns signed pacs.002 response

**Validation Points:**
- ✅ HTTP Status: 200 OK
- ✅ Response contains signed FPEnvelope
- ✅ Response contains pacs.002 document
- ✅ Transaction persisted with Status = Pending or Success
- ✅ CoreBank callback was invoked

**Database Checks:**
```sql
SELECT * FROM iso_messages WHERE TxId = 'AGROSOS0528910638962089436554484';
-- Should show Status = Success or Pending
```

### Test Scenario 2: CoreBank Callback Failure

**Setup:** Configure CoreBank endpoint to return error or be unavailable

**Expected Behavior:**
1. Handler processes message
2. CoreBank callback fails
3. Transaction marked as Failed or Pending
4. Returns appropriate response

**Validation Points:**
- ✅ HTTP Status: 200 OK (handler still returns response)
- ✅ Transaction Status = Failed or Pending
- ✅ Error logged in system

---

## IncomingVerificationHandler

**Purpose:** Processes incoming acmt.023 verification requests

### Test Scenario 1: Successful Verification

**Payload:** `Payloads/acmt.023.xml`

**Expected Behavior:**
1. Handler validates XML signature
2. Parses acmt.023 message
3. Calls verification service
4. Returns signed acmt.024 response

**Validation Points:**
- ✅ HTTP Status: 200 OK
- ✅ Response contains signed FPEnvelope
- ✅ Response contains acmt.024 document
- ✅ Verification result persisted

### Test Scenario 2: Verification Service Unavailable

**Setup:** Configure verification service to be unavailable

**Expected Behavior:**
1. Handler processes message
2. Verification service call fails
3. Returns error response

**Validation Points:**
- ✅ HTTP Status: 200 OK or 500
- ✅ Error logged
- ✅ Appropriate error message in response

---

## IncomingTransactionStatusHandler

**Purpose:** Processes incoming pacs.002 transaction status reports

### Test Scenario 1: Status Update for Existing Transaction

**Payload:** `Payloads/pacs.002-status.xml`

**Prerequisites:**
- Transaction with TxId exists in database
- Transaction Status = Pending

**Expected Behavior:**
1. Handler validates XML signature
2. Parses pacs.002 message
3. Retrieves existing transaction
4. Updates transaction status
5. Returns signed response

**Validation Points:**
- ✅ HTTP Status: 200 OK
- ✅ Transaction status updated in database
- ✅ ISOMessageStatus record created
- ✅ Response contains acknowledgment

**Database Checks:**
```sql
SELECT * FROM iso_message_statuses 
WHERE ISOMessageId = (SELECT Id FROM iso_messages WHERE TxId = 'TX-TEST-001')
ORDER BY CreatedAt DESC LIMIT 1;
-- Should show latest status update
```

### Test Scenario 2: Status for Non-Existent Transaction

**Payload:** `Payloads/pacs.002-payment-status-notfound.xml`

**Expected Behavior:**
1. Handler attempts to retrieve transaction
2. Transaction not found
3. Returns admi.002 error response

**Validation Points:**
- ✅ HTTP Status: 200 OK (with admi.002) or 404
- ✅ Response indicates transaction not found
- ✅ No database changes

---

## IncomingPaymentStatusReportHandler

**Purpose:** Processes pacs.002 payment status reports with business logic

### Test Scenario 1: ACSC Status - CoreBank Success

**Payload:** `Payloads/pacs.002-payment-status-acsc.xml`

**Prerequisites:**
- Transaction with matching TxId exists
- Transaction Status = Pending

**Expected Behavior:**
1. Handler validates and parses message
2. Retrieves transaction from database
3. Calls CoreBank callback with payment details
4. CoreBank returns success
5. Updates transaction Status = Success
6. Returns signed response

**Validation Points:**
- ✅ HTTP Status: 200 OK
- ✅ Transaction Status = Success
- ✅ Transaction Reason contains "Processed" or success message
- ✅ CoreBank callback invoked
- ✅ ISOMessageStatus persisted

**Database Checks:**
```sql
SELECT Status, Reason, AdditionalInfo 
FROM iso_messages 
WHERE TxId = 'TX-ACSC-001';
-- Status should be 'Success'
-- Reason should contain success message
```

### Test Scenario 2: ACSC Status - CoreBank Failure

**Payload:** `Payloads/pacs.002-payment-status-acsc.xml`

**Setup:** Configure CoreBank to return error

**Expected Behavior:**
1. Handler processes message
2. Calls CoreBank callback
3. CoreBank returns error
4. Updates transaction Status = ReadyForReturn
5. Returns signed response

**Validation Points:**
- ✅ HTTP Status: 200 OK
- ✅ Transaction Status = ReadyForReturn
- ✅ Transaction Reason = "CoreBank callback failed"
- ✅ Manual intervention required

**Database Checks:**
```sql
SELECT Status, Reason 
FROM iso_messages 
WHERE TxId = 'TX-ACSC-001';
-- Status should be 'ReadyForReturn'
-- Reason should indicate CoreBank failure
```

### Test Scenario 3: RJCT Status

**Payload:** `Payloads/pacs.002-payment-status-rjct.xml`

**Prerequisites:**
- Transaction with matching TxId exists
- Transaction Status = Pending

**Expected Behavior:**
1. Handler validates and parses message
2. Detects RJCT status
3. Updates transaction Status = Failed
4. Does NOT call CoreBank (payment rejected by IPS)
5. Returns signed response

**Validation Points:**
- ✅ HTTP Status: 200 OK
- ✅ Transaction Status = Failed
- ✅ Transaction Reason = "Received rejection confirmation"
- ✅ CoreBank callback NOT invoked
- ✅ Rejection reason captured in AdditionalInfo

**Database Checks:**
```sql
SELECT Status, Reason, AdditionalInfo 
FROM iso_messages 
WHERE TxId = 'TX-RJCT-001';
-- Status should be 'Failed'
-- Reason should indicate rejection
-- AdditionalInfo should contain rejection details (e.g., "AM04 - Insufficient funds")
```

### Test Scenario 4: Idempotency - Duplicate ACSC for Success Transaction

**Payload:** `Payloads/pacs.002-payment-status-acsc.xml` (send twice)

**Prerequisites:**
- First request already processed successfully
- Transaction Status = Success

**Expected Behavior:**
1. Handler detects transaction already successful
2. Does NOT call CoreBank again
3. Persists status for audit trail
4. Returns signed response

**Validation Points:**
- ✅ HTTP Status: 200 OK
- ✅ Transaction Status remains Success
- ✅ CoreBank callback NOT invoked (idempotency)
- ✅ New ISOMessageStatus record created (audit trail)

### Test Scenario 5: Idempotency - Duplicate ACSC for Failed Transaction

**Payload:** `Payloads/pacs.002-payment-status-acsc.xml`

**Prerequisites:**
- Transaction Status = Failed (from previous RJCT)

**Expected Behavior:**
1. Handler detects transaction already failed
2. Does NOT process again
3. Returns signed response

**Validation Points:**
- ✅ HTTP Status: 200 OK
- ✅ Transaction Status remains Failed
- ✅ No business logic re-executed

### Test Scenario 6: Transaction Not Found

**Payload:** `Payloads/pacs.002-payment-status-notfound.xml`

**Expected Behavior:**
1. Handler attempts to retrieve transaction
2. Transaction not found in database
3. Returns admi.002 error response

**Validation Points:**
- ✅ HTTP Status: 200 OK (with admi.002) or 404
- ✅ Response indicates transaction not found
- ✅ Error logged

### Test Scenario 7: Invalid Signature

**Payload:** `Payloads/pacs.008-invalid-signature.xml`

**Expected Behavior:**
1. Handler attempts signature validation
2. Signature validation fails
3. Returns error response

**Validation Points:**
- ✅ HTTP Status: 401 Unauthorized or 400 Bad Request
- ✅ Error message indicates signature failure
- ✅ No database changes

---

## Return Completion Scenarios

### Test Scenario 8: Return Completion (Future Feature)

**Note:** Current implementation does NOT support automatic return completion.

**Payload:** `Payloads/pacs.002-payment-status-acsc.xml`

**Prerequisites:**
- Transaction Status = ReadyForReturn

**Current Behavior:**
1. Handler processes ACSC status
2. Transaction remains ReadyForReturn
3. CoreBank Return endpoint NOT called automatically

**Future Behavior (when implemented):**
1. Handler detects ReadyForReturn status
2. Calls CoreBank Return endpoint
3. If successful, updates Status = Success
4. If failed, keeps Status = ReadyForReturn

---

## Performance Testing

### Load Test Scenario

**Objective:** Test handler performance under load

**Setup:**
1. Use Apache JMeter or similar tool
2. Send 100 concurrent requests
3. Monitor response times and error rates

**Validation Points:**
- ✅ Average response time < 500ms
- ✅ 95th percentile < 1000ms
- ✅ Error rate < 1%
- ✅ No database deadlocks
- ✅ All transactions processed correctly

---

## Integration Testing Checklist

### Before Testing
- [ ] API project is running
- [ ] Database is accessible and seeded with test data
- [ ] CoreBank mock/test endpoints are configured
- [ ] Verification service is available
- [ ] Logging is enabled
- [ ] Test certificates/keys are configured

### During Testing
- [ ] Monitor application logs
- [ ] Check database for expected changes
- [ ] Verify CoreBank callbacks are made
- [ ] Validate XML signatures in responses
- [ ] Check for memory leaks or performance issues

### After Testing
- [ ] Review test results
- [ ] Clean up test data
- [ ] Document any issues found
- [ ] Update test scenarios as needed

---

## Troubleshooting Guide

### Issue: Signature Validation Fails

**Possible Causes:**
- Incorrect public key configuration
- XML signature malformed
- Certificate expired

**Resolution:**
1. Verify public key in configuration
2. Check certificate validity
3. Validate XML signature format

### Issue: Transaction Not Found

**Possible Causes:**
- Transaction not seeded in database
- TxId mismatch
- Database connection issue

**Resolution:**
1. Check database for transaction
2. Verify TxId in payload matches database
3. Test database connectivity

### Issue: CoreBank Callback Fails

**Possible Causes:**
- CoreBank endpoint unavailable
- Network connectivity issue
- Invalid request format

**Resolution:**
1. Verify CoreBank endpoint URL
2. Check network connectivity
3. Review CoreBank logs
4. Validate request payload format

---

## Test Data Management

### Creating Test Transactions

```sql
-- Insert test transaction
INSERT INTO iso_messages (TxId, EndToEndId, BizMsgIdr, MsgDefIdr, MsgId, Status, CreatedAt)
VALUES ('TX-TEST-001', 'E2E-TEST-001', 'MSG-TEST-001', 'pacs.008.001.10', 'MSG-001', 'Pending', NOW());

-- Insert transaction details
INSERT INTO transactions (ISOMessageId, Amount, Currency, DebtorName, CreditorName, ...)
VALUES (...);
```

### Cleaning Up Test Data

```sql
-- Remove test transactions
DELETE FROM iso_message_statuses WHERE ISOMessageId IN (SELECT Id FROM iso_messages WHERE TxId LIKE 'TX-TEST-%');
DELETE FROM transactions WHERE ISOMessageId IN (SELECT Id FROM iso_messages WHERE TxId LIKE 'TX-TEST-%');
DELETE FROM iso_messages WHERE TxId LIKE 'TX-TEST-%';
```

---

## Appendix: Status Codes Reference

### Transaction Status Values
- **Pending:** Initial state, awaiting processing
- **Success:** Transaction completed successfully
- **Failed:** Transaction failed (rejected or error)
- **ReadyForReturn:** Awaiting manual return processing

### HTTP Status Codes
- **200 OK:** Request processed successfully
- **400 Bad Request:** Invalid request format
- **401 Unauthorized:** Invalid signature
- **404 Not Found:** Transaction not found
- **500 Internal Server Error:** Handler processing error

### ISO 20022 Status Codes
- **ACSC:** AcceptedSettlementCompleted
- **RJCT:** Rejected
- **PDNG:** Pending
