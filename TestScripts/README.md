# SIPS Handler Test Scripts - Enhanced

This directory contains enhanced test scripts and sample payloads for testing SIPS handlers from the API project.

## Overview

These scripts allow you to test the following handlers:

1. **IncomingTransactionHandler** - Processes incoming pacs.008 payment requests
2. **IncomingVerificationHandler** - Processes acmt.023 verification requests
3. **IncomingTransactionStatusHandler** - Processes incoming pacs.002 status reports
4. **IncomingPaymentStatusReportHandler** - Processes pacs.002 payment status reports

## What's New in Enhanced Version

✨ **New Features:**

- ✅ Correct API endpoints (`/api/v1/incoming`)
- ✅ Comprehensive test reporting with statistics
- ✅ JSON output for CI/CD integration
- ✅ Retry logic for failed tests
- ✅ Response validation (XML structure, transaction IDs)
- ✅ Configurable timeouts
- ✅ Verbose mode for detailed debugging
- ✅ Color-coded output for better readability
- ✅ Test duration tracking
- ✅ Exit codes for automation

## Directory Structure

````
TestScripts/
├── README.md                           # This file
├── test-config.json                    # Test configuration file
├── Payloads/                          # Sample XML payloads
│   ├── pacs.008.xml                   # Payment request
│   ├── acmt.023.xml                   # Verification request
│   ├── pacs.002-status.xml            # Transaction status
│   ├── pacs.002-payment-status.xml    # Payment status report
│   ├── pacs.002-payment-status-acsc.xml  # Success scenario
│   ├── pacs.002-payment-status-rjct.xml  # Rejection scenario
│   └── pacs.002-payment-status-notfound.xml  # Not found scenario
├── curl-examples.sh                   # Enhanced bash test script
├── test-handlers.ps1                  # Enhanced PowerShell test script
├── postman-collection.json            # Postman collection
└── test-scenarios.md                  # Detailed test scenarios

## Quick Start

### Prerequisites

1. Your API project is running (e.g., `http://localhost:5000`)
2. You have the necessary authentication/API keys configured (if required)
3. The handlers are properly registered in your API project

### Using Enhanced Bash Script

```bash
# Basic usage
./curl-examples.sh http://localhost:5000

# With JSON output for CI/CD
./curl-examples.sh http://localhost:5000 --json-output

# With verbose output for debugging
./curl-examples.sh http://localhost:5000 --verbose

# With retry logic (retry failed tests 3 times)
./curl-examples.sh http://localhost:5000 --retry 3

# With custom timeout (60 seconds)
./curl-examples.sh http://localhost:5000 --timeout 60

# All options combined
./curl-examples.sh http://localhost:5000 --json-output --verbose --retry 3 --timeout 60
````

### Using Enhanced PowerShell Script

```powershell
# Basic usage
.\test-handlers.ps1 -BaseUrl "http://localhost:5000"

# With JSON output for CI/CD
.\test-handlers.ps1 -BaseUrl "http://localhost:5000" -JsonOutput

# With verbose output for debugging
.\test-handlers.ps1 -BaseUrl "http://localhost:5000" -VerboseOutput

# With retry logic (retry failed tests 3 times)
.\test-handlers.ps1 -BaseUrl "http://localhost:5000" -RetryCount 3

# With custom timeout (60 seconds)
.\test-handlers.ps1 -BaseUrl "http://localhost:5000" -Timeout 60

# All options combined
.\test-handlers.ps1 -BaseUrl "http://localhost:5000" -JsonOutput -VerboseOutput -RetryCount 3 -Timeout 60
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

To add new test scenarios:

**In bash script (`curl-examples.sh`):**

```bash
test_handler \
    "Your Test Name" \
    "/api/v1/incoming" \
    "$PAYLOADS_DIR/your-payload.xml" \
    200
```

**In PowerShell script (`test-handlers.ps1`):**

```powershell
Test-Handler `
    -Name "Your Test Name" `
    -Endpoint "/api/v1/incoming" `
    -PayloadFile (Join-Path $PayloadsDir "your-payload.xml") `
    -ExpectedStatus 200
```

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
          chmod +x curl-examples.sh
          ./curl-examples.sh http://localhost:5000 --json-output --retry 2

      - name: Upload Test Report
        if: always()
        uses: actions/upload-artifact@v3
        with:
          name: test-report
          path: TestScripts/test-report-*.json
```

### Azure DevOps Example

```yaml
steps:
  - task: PowerShell@2
    displayName: "Run API Tests"
    inputs:
      targetType: "filePath"
      filePath: "TestScripts/test-handlers.ps1"
      arguments: '-BaseUrl "http://localhost:5000" -JsonOutput -RetryCount 2'

  - task: PublishTestResults@2
    condition: always()
    inputs:
      testResultsFormat: "JUnit"
      testResultsFiles: "TestScripts/test-report-*.json"
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
# Bash - verbose mode
./curl-examples.sh http://localhost:5000 --verbose

# PowerShell - verbose mode
.\test-handlers.ps1 -BaseUrl "http://localhost:5000" -VerboseOutput
```

## Additional Resources

- **test-scenarios.md** - Detailed test scenarios and validation points
- **test-config.json** - Configuration file for test settings
- **CONTEXT_FOR_API_PROJECT.md** - Integration guide for API projects
- **QUICK_START.md** - Quick start guide
- **ENVIRONMENT_SETUP.md** - Environment setup instructions

## Features Comparison

| Feature              | Basic Scripts | Enhanced Scripts |
| -------------------- | ------------- | ---------------- |
| Correct endpoints    | ❌            | ✅               |
| Test statistics      | ❌            | ✅               |
| JSON output          | ❌            | ✅               |
| Retry logic          | ❌            | ✅               |
| Response validation  | ❌            | ✅               |
| Configurable timeout | ❌            | ✅               |
| Verbose mode         | ❌            | ✅               |
| Exit codes           | ❌            | ✅               |
| CI/CD ready          | ❌            | ✅               |

## Support

For issues or questions:

1. Check the troubleshooting section above
2. Review test-scenarios.md for expected behaviors
3. Check API logs for detailed error messages
4. Verify test-config.json settings
