# SIPS Handler Test Scripts - Complete Package

## 📦 What's Included

This test scripts package provides everything you need to test SIPS handlers from an external API project.

### Directory Structure

```
TestScripts/
├── README.md                              # Main documentation
├── QUICK_START.md                         # 5-minute quick start guide
├── TEST_SCRIPTS_SUMMARY.md               # This file
├── test-scenarios.md                      # Detailed test scenarios (17 scenarios)
├── curl-examples.sh                       # Bash test script (Mac/Linux)
├── test-handlers.ps1                      # PowerShell test script (Windows)
├── postman-collection.json                # Postman collection
└── Payloads/                             # Sample XML payloads
    ├── pacs.008.xml                       # Payment request
    ├── acmt.023.xml                       # Verification request
    ├── pacs.002-status.xml                # Transaction status
    ├── pacs.002-payment-status.xml        # Generic payment status
    ├── pacs.002-payment-status-acsc.xml   # Success status
    ├── pacs.002-payment-status-rjct.xml   # Rejection status
    └── pacs.002-payment-status-notfound.xml # Not found scenario
```

---

## 🎯 Handlers Covered

### 1. IncomingTransactionHandler
- **Message Type:** pacs.008 (FIToFICustomerCreditTransfer)
- **Purpose:** Process incoming payment requests
- **Test Scenarios:** 2
- **Payload:** `Payloads/pacs.008.xml`

### 2. IncomingVerificationHandler
- **Message Type:** acmt.023 (IdentificationVerificationRequest)
- **Purpose:** Process verification requests
- **Test Scenarios:** 2
- **Payload:** `Payloads/acmt.023.xml`

### 3. IncomingTransactionStatusHandler
- **Message Type:** pacs.002 (FIToFIPaymentStatusReport)
- **Purpose:** Process transaction status updates
- **Test Scenarios:** 2
- **Payload:** `Payloads/pacs.002-status.xml`

### 4. IncomingPaymentStatusReportHandler
- **Message Type:** pacs.002 (FIToFIPaymentStatusReport)
- **Purpose:** Process payment status with business logic
- **Test Scenarios:** 8
- **Payloads:** Multiple variants (ACSC, RJCT, NotFound)

**Total Test Scenarios:** 17

---

## 🚀 Quick Start

### For Mac/Linux Users

```bash
cd TestScripts
chmod +x curl-examples.sh
./curl-examples.sh http://localhost:5000
```

### For Windows Users

```powershell
cd TestScripts
.\test-handlers.ps1 -BaseUrl "http://localhost:5000"
```

### For Postman Users

1. Import `postman-collection.json`
2. Set `{{baseUrl}}` variable
3. Run collection

---

## 📋 Test Coverage

### Happy Path Tests ✅
- ✅ Successful payment processing
- ✅ Successful verification
- ✅ ACSC status with CoreBank success
- ✅ Status updates for existing transactions

### Error Handling Tests ✅
- ✅ RJCT status (rejection)
- ✅ Transaction not found
- ✅ Invalid signature
- ✅ CoreBank callback failures
- ✅ Invalid XML format
- ✅ Empty request body

### Business Logic Tests ✅
- ✅ Idempotency (duplicate requests)
- ✅ Status transitions (Pending → Success/Failed)
- ✅ CoreBank integration
- ✅ Return completion scenarios

### Edge Cases ✅
- ✅ Already successful transactions
- ✅ Already failed transactions
- ✅ ReadyForReturn status
- ✅ Null responses from CoreBank

---

## 📊 Expected Results

### Success Responses

All successful requests should return:
- **HTTP Status:** 200 OK
- **Content-Type:** application/xml
- **Body:** Signed FPEnvelope with appropriate response message

### Error Responses

| Scenario | HTTP Status | Response Type |
|----------|-------------|---------------|
| Invalid XML | 400 Bad Request | Error message |
| Invalid Signature | 401 Unauthorized | Error message |
| Transaction Not Found | 404 Not Found or 200 with admi.002 | Error message |
| Handler Error | 500 Internal Server Error | Error message |

---

## 🔍 Validation Points

### For Each Test

1. **HTTP Response**
   - Check status code
   - Verify content type
   - Validate response structure

2. **Database Changes**
   - Transaction status updated
   - ISOMessageStatus records created
   - Timestamps accurate

3. **External Calls**
   - CoreBank callbacks invoked (where applicable)
   - Verification service called (where applicable)

4. **Logging**
   - Appropriate log entries created
   - Error details captured
   - Audit trail maintained

---

## 🛠️ Customization Guide

### Update Endpoints

Edit the scripts to match your API routing:

**Bash Script (`curl-examples.sh`):**
```bash
BASE_URL="${1:-http://localhost:5000}"
```

**PowerShell Script (`test-handlers.ps1`):**
```powershell
param([string]$BaseUrl = "http://localhost:5000")
```

**Postman Collection:**
```json
"variable": [{"key": "baseUrl", "value": "http://localhost:5000"}]
```

### Update Test Data

Edit XML files in `Payloads/` to match your test environment:

1. **Transaction IDs:** Change `<document:OrgnlTxId>` values
2. **Amounts:** Modify `<document:IntrBkSttlmAmt>` values
3. **Account Numbers:** Update debtor/creditor account IDs
4. **Dates:** Adjust timestamps as needed

### Add New Scenarios

1. Copy an existing payload file
2. Modify the relevant fields
3. Add a new test case to the scripts
4. Document the scenario in `test-scenarios.md`

---

## 📈 Integration with CI/CD

### GitHub Actions Example

```yaml
name: Integration Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      
      - name: Start API
        run: |
          cd /path/to/api
          dotnet run &
          sleep 10
      
      - name: Run Handler Tests
        run: |
          cd SIPS.Core.Tests/TestScripts
          chmod +x curl-examples.sh
          ./curl-examples.sh http://localhost:5000
```

### Jenkins Pipeline Example

```groovy
pipeline {
    agent any
    stages {
        stage('Start API') {
            steps {
                sh 'dotnet run --project /path/to/api &'
                sleep 10
            }
        }
        stage('Run Tests') {
            steps {
                sh '''
                    cd SIPS.Core.Tests/TestScripts
                    chmod +x curl-examples.sh
                    ./curl-examples.sh http://localhost:5000
                '''
            }
        }
    }
}
```

---

## 📚 Documentation Files

### Quick Reference
- **QUICK_START.md** - Get started in 5 minutes
- **README.md** - Complete documentation

### Detailed Guides
- **test-scenarios.md** - All 17 test scenarios with validation points
- **TEST_SCRIPTS_SUMMARY.md** - This file

### Scripts
- **curl-examples.sh** - Automated bash testing
- **test-handlers.ps1** - Automated PowerShell testing
- **postman-collection.json** - Postman collection

---

## 🎓 Learning Resources

### Understanding the Handlers

Each handler follows this pattern:
1. **Validate** XML signature
2. **Parse** ISO 20022 message
3. **Process** business logic
4. **Persist** changes to database
5. **Return** signed response

### Key Concepts

- **ISO 20022:** International standard for financial messaging
- **pacs.008:** Payment initiation message
- **pacs.002:** Payment status report
- **acmt.023:** Verification request
- **FPEnvelope:** Wrapper for ISO 20022 messages
- **XML Signature:** Digital signature for message authentication

---

## ✅ Testing Checklist

### Before Running Tests
- [ ] API project is running
- [ ] Database is accessible
- [ ] Test data is seeded
- [ ] CoreBank endpoints configured
- [ ] Certificates/keys configured
- [ ] Logging enabled

### During Testing
- [ ] Monitor application logs
- [ ] Check database changes
- [ ] Verify external calls
- [ ] Validate response signatures
- [ ] Check performance metrics

### After Testing
- [ ] Review test results
- [ ] Document failures
- [ ] Clean up test data
- [ ] Update test scenarios
- [ ] Archive test logs

---

## 🔧 Troubleshooting

### Common Issues

1. **Connection Refused**
   - Ensure API is running
   - Check port number
   - Verify firewall settings

2. **Signature Validation Fails**
   - Check public key configuration
   - Verify certificate validity
   - Validate XML signature format

3. **Transaction Not Found**
   - Seed test data in database
   - Verify TxId matches
   - Check database connectivity

4. **CoreBank Callback Fails**
   - Verify endpoint URL
   - Check network connectivity
   - Review CoreBank logs

See `QUICK_START.md` for detailed troubleshooting steps.

---

## 📞 Support

### Getting Help

1. **Review Documentation**
   - Start with `QUICK_START.md`
   - Check `test-scenarios.md` for specific scenarios
   - Review `README.md` for complete reference

2. **Check Logs**
   - Application logs for errors
   - Database logs for query issues
   - Network logs for connectivity problems

3. **Validate Setup**
   - Verify API is running
   - Check database connectivity
   - Confirm configuration settings

---

## 🎉 Success Metrics

Your testing is successful when:

- ✅ **All scripts run without errors**
- ✅ **All HTTP responses are 200 OK** (for valid requests)
- ✅ **Database shows expected changes**
- ✅ **No errors in application logs**
- ✅ **External services called correctly**
- ✅ **Response times are acceptable** (< 1 second)
- ✅ **All 17 test scenarios pass**

---

## 🚀 Next Steps

1. **Run Quick Start** - Get familiar with the basics
2. **Explore Scenarios** - Review all 17 test scenarios
3. **Customize Tests** - Adapt to your environment
4. **Automate** - Integrate into CI/CD pipeline
5. **Monitor** - Set up logging and monitoring
6. **Iterate** - Add new scenarios as needed

---

**Happy Testing! 🎯**

*This test package ensures your SIPS handlers work correctly in production environments.*
