# SIPS Handler Test Scripts

This directory contains automated test scripts and sample payloads for testing SIPS handlers from the API project.

## Overview

These scripts allow you to test the following handlers:

1. **IncomingTransactionHandler** - Processes incoming pacs.008 payment requests
2. **IncomingVerificationHandler** - Processes acmt.023 verification requests
3. **IncomingTransactionStatusHandler** - Processes incoming pacs.002 status reports
4. **IncomingPaymentStatusReportHandler** - Processes pacs.002 payment status reports

## Key Features

✨ **Test Automation:**

- ✅ Automated test runner with comprehensive reporting
- ✅ JSON output for CI/CD integration
- ✅ Retry logic for failed tests
- ✅ Response validation (XML structure, transaction IDs)
- ✅ Configurable timeouts
- ✅ Verbose mode for detailed debugging
- ✅ Color-coded output for better readability
- ✅ Test duration tracking
- ✅ Exit codes for automation
- ✅ Health check utilities

## Directory Structure

```
TestScripts/
├── README.md                           # This file
├── QUICK_START.md                      # Quick start guide
├── QUICK_REFERENCE.md                  # Quick reference for common commands
├── TEST_RUNNER_GUIDE.md                # Detailed test runner documentation
├── TROUBLESHOOTING.md                  # Troubleshooting guide
├── AUTOMATED_TESTING_SUMMARY.md        # Automated testing overview
├── JSON_PAYLOADS_SUMMARY.md            # JSON payload documentation
├── PAYLOAD_UPDATE_SUMMARY.md           # Payload update history
├── test-config.json                    # Test configuration file
├── test-scenarios.md                   # Detailed test scenarios
├── Payloads/                           # Sample XML and JSON payloads
│   ├── pacs.008.xml                    # Payment request
│   ├── acmt.023.xml                    # Verification request
│   ├── pacs.002-status.xml             # Transaction status
│   ├── pacs.002-payment-status.xml     # Payment status report
│   ├── pacs.002-payment-status-acsc.xml # Success scenario
│   ├── pacs.002-payment-status-rjct.xml # Rejection scenario
│   ├── pacs.002-payment-status-notfound.xml # Not found scenario
│   └── [JSON payloads for Gateway APIs]
├── run-all-tests.sh                    # Main test runner script
├── check-api.sh                        # API health check script
├── test-health.sh                      # Health endpoint test
├── postman-collection.json             # Postman collection
└── test-reports/                       # Generated test reports

## Quick Start

### Prerequisites

1. Your API project is running (e.g., `http://localhost:5000`)
2. You have the necessary authentication/API keys configured (if required)
3. The handlers are properly registered in your API project

### Using the Main Test Runner

```bash
# Basic usage - runs all tests
./run-all-tests.sh

# Specify custom base URL
./run-all-tests.sh http://localhost:5000

# With verbose output for debugging
./run-all-tests.sh http://localhost:5000 --verbose

# With retry logic (retry failed tests 3 times)
./run-all-tests.sh http://localhost:5000 --retry 3

# With custom timeout (60 seconds)
./run-all-tests.sh http://localhost:5000 --timeout 60

# All options combined
./run-all-tests.sh http://localhost:5000 --verbose --retry 3 --timeout 60
```

### Check API Health

```bash
# Check if API is running and healthy
./check-api.sh

# Check specific URL
./check-api.sh https://localhost:443

# Test health endpoint
./test-health.sh https://localhost:443
```

### Using Individual cURL Commands

```bash
# All handlers use the same endpoint: /api/v1/incoming
# The API automatically routes to the correct handler based on message type

# Test IncomingTransactionHandler (pacs.008)
curl -X POST http://localhost:5000/api/v1/incoming \
  -H "Content-Type: application/xml" \
  -d @Payloads/pacs.008.xml

# Test IncomingVerificationHandler (acmt.023)
curl -X POST http://localhost:5000/api/v1/incoming \
  -H "Content-Type: application/xml" \
  -d @Payloads/acmt.023.xml

# Test IncomingTransactionStatusHandler (pacs.002)
curl -X POST http://localhost:5000/api/v1/incoming \
  -H "Content-Type: application/xml" \
  -d @Payloads/pacs.002-status.xml

# Test IncomingPaymentStatusReportHandler (pacs.002)
curl -X POST http://localhost:5000/api/v1/incoming \
  -H "Content-Type: application/xml" \
  -d @Payloads/pacs.002-payment-status.xml
```

### Using Postman

1. Import `postman-collection.json` into Postman
2. Update the `{{baseUrl}}` variable to your API endpoint (e.g., `http://localhost:5000`)
3. Run the collection or individual requests

### Using Test Configuration

Edit `test-config.json` to customize:

- API base URLs for different environments
- Timeout and retry settings
- Payload file locations
- Validation options

## Expected Responses

### Success Scenarios

All handlers should return a signed XML envelope on success:

- **Status Code:** 200 OK
- **Content-Type:** application/xml
- **Body:** Signed FPEnvelope with appropriate response message
- **Validation:** Response contains `<FPEnvelope>` and transaction ID

### Error Scenarios

- **400 Bad Request:** Invalid XML format or missing required fields
- **401 Unauthorized:** Invalid signature
- **404 Not Found:** Transaction not found (for status handlers)
- **500 Internal Server Error:** Handler processing error

## Test Output

### Console Output

The enhanced scripts provide detailed console output:

```
==========================================
SIPS Handler Test Scripts - Enhanced
==========================================
Base URL: http://localhost:5000
Timeout: 30s
Retry Count: 0
JSON Output: false
Verbose: false
Start Time: 2024-11-16 10:35:00
==========================================

=== Core Handler Tests ===

[Test 1] IncomingTransactionHandler (pacs.008)
Endpoint: http://localhost:5000/api/v1/incoming
Payload: pacs.008.xml
✓ SUCCESS (HTTP 200) [0.45s]
  Valid XML response | TxId: AGROSOS0528910638962089436554484

------------------------------------------

...

==========================================
Test Summary
==========================================
Total Tests:    7
Passed:         7
Failed:         0
Success Rate:   100.0%
Total Duration: 3s
End Time:       2024-11-16 10:35:03
==========================================
```

### JSON Report Output

When using `--json-output` or `-JsonOutput`, a JSON report is generated:

```json
{
  "summary": {
    "total": 7,
    "passed": 7,
    "failed": 0,
    "success_rate": 100.0,
    "duration": 3.2,
    "start_time": "2024-11-16 10:35:00",
    "end_time": "2024-11-16 10:35:03",
    "base_url": "http://localhost:5000"
  },
  "tests": [
    {
      "test": "IncomingTransactionHandler (pacs.008)",
      "status": "passed",
      "http_code": 200,
      "response_time": 0.45,
      "duration": 1
    }
  ]
}
```

### Exit Codes

- **0:** All tests passed
- **1:** One or more tests failed

This makes the scripts perfect for CI/CD pipelines.

## Test Data

All sample payloads use test data:

- **Sender BIC:** SENDERBIC
- **Receiver BIC:** RECEIVERBIC
- **Test Transaction IDs:** TX-TEST-001, TX-TEST-002, etc.
- **Test Amounts:** Various amounts in USD

## Customization

### Customizing Test Data

1. Edit the XML files in `Payloads/`
2. Update transaction IDs, amounts, account numbers as needed
3. Ensure your test database has matching records for status queries

### Customizing Test Configuration

Edit `test-config.json` to:

- Change default base URLs
- Configure different environments (local, dev, staging, production)
- Adjust timeout and retry settings
- Enable/disable validation options

### Adding New Tests

To add new test scenarios, edit `run-all-tests.sh` and add your test:

```bash
# Add to the test runner script
test_handler \
    "Your Test Name" \
    "/api/v1/incoming" \
    "$PAYLOADS_DIR/your-payload.xml" \
    200
```

Or create a new payload file in the `Payloads/` directory and the test runner will automatically detect it based on the configuration in `test-config.json`.

## CI/CD Integration

### GitHub Actions Example

```yaml
name: API Integration Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - name: Start API
        run: |
          dotnet run --project SIPS.Connect.csproj &
          sleep 10

      - name: Run Tests
        run: |
          cd TestScripts
          chmod +x run-all-tests.sh
          ./run-all-tests.sh http://localhost:5000 --retry 2

      - name: Upload Test Report
        if: always()
        uses: actions/upload-artifact@v3
        with:
          name: test-report
          path: TestScripts/test-reports/*.json
```

### GitLab CI Example

```yaml
test:
  stage: test
  script:
    - cd TestScripts
    - chmod +x run-all-tests.sh
    - ./run-all-tests.sh http://localhost:5000 --retry 2
  artifacts:
    when: always
    paths:
      - TestScripts/test-reports/
    reports:
      junit: TestScripts/test-reports/*.xml
```

## Troubleshooting

### Common Issues

1. **Connection Refused**

   - Ensure the API is running on the specified port
   - Check firewall settings
   - Verify the base URL is correct

2. **Timeout Errors**

   - Increase timeout: `--timeout 60` or `-Timeout 60`
   - Check API performance and database connectivity

3. **Signature Validation Fails**

   - Ensure your API has the correct public key configured
   - Check that the XML signature is properly formatted
   - Verify certificate validity

4. **Transaction Not Found**

   - Verify the transaction exists in your database
   - Check the transaction ID matches exactly
   - Ensure database is seeded with test data

5. **All Tests Failing**
   - Verify API endpoint: `/api/v1/incoming` (not `/api/incoming/*`)
   - Check API logs for errors
   - Ensure handlers are registered in DI container
   - Verify database connection string

### Debug Mode

For detailed debugging:

```bash
# Verbose mode with detailed output
./run-all-tests.sh http://localhost:5000 --verbose

# Check API connectivity
./check-api.sh http://localhost:5000

# Test health endpoint
./test-health.sh http://localhost:5000
```

## Additional Resources

- **QUICK_START.md** - Quick start guide for getting started
- **QUICK_REFERENCE.md** - Quick reference for common commands
- **TEST_RUNNER_GUIDE.md** - Detailed test runner documentation
- **TROUBLESHOOTING.md** - Comprehensive troubleshooting guide
- **test-scenarios.md** - Detailed test scenarios and validation points
- **test-config.json** - Configuration file for test settings
- **AUTOMATED_TESTING_SUMMARY.md** - Automated testing overview
- **JSON_PAYLOADS_SUMMARY.md** - JSON payload documentation
- **PAYLOAD_UPDATE_SUMMARY.md** - Payload update history

## Test Runner Features

| Feature              | Status |
| -------------------- | ------ |
| Automated test execution | ✅ |
| Test statistics & reporting | ✅ |
| JSON output for CI/CD | ✅ |
| Retry logic | ✅ |
| Response validation | ✅ |
| Configurable timeout | ✅ |
| Verbose mode | ✅ |
| Exit codes for automation | ✅ |
| Health checks | ✅ |
| XML & JSON payload support | ✅ |

## Support

For issues or questions:

1. Check the troubleshooting section above
2. Review test-scenarios.md for expected behaviors
3. Check API logs for detailed error messages
4. Verify test-config.json settings
