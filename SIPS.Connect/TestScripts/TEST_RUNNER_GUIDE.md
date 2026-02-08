# SIPS Connect - Automated Test Runner Guide

**Version:** 1.0  
**Last Updated:** November 28, 2024

---

## 🚀 Quick Start

### Basic Usage (No Authentication)

```bash
# Run all tests without authentication
./run-all-tests.sh --skip-auth

# Run with custom URL
./run-all-tests.sh --url https://localhost:443 --skip-auth
```

### With Authentication

```bash
# Run with API key authentication
./run-all-tests.sh \
  --url http://localhost:5000 \
  --api-key "your-api-key" \
  --api-secret "your-api-secret"
```

### Generate Reports

```bash
# Generate both JSON and HTML reports (default)
./run-all-tests.sh --skip-auth

# Generate only JSON report
./run-all-tests.sh --skip-auth --format json

# Generate only HTML report
./run-all-tests.sh --skip-auth --format html

# Custom report directory
./run-all-tests.sh --skip-auth --report-dir ./my-reports
```

---

## 📋 Command Line Options

| Option | Description | Default | Example |
|--------|-------------|---------|---------|
| `--url URL` | API base URL | `http://localhost:5000` | `--url https://api.example.com` |
| `--api-key KEY` | API key for authentication | None | `--api-key "abc123"` |
| `--api-secret SECRET` | API secret for authentication | None | `--api-secret "xyz789"` |
| `--skip-auth` | Skip authentication headers | false | `--skip-auth` |
| `--report-dir DIR` | Report output directory | `./test-reports` | `--report-dir ./reports` |
| `--format FORMAT` | Report format: json, html, both | `both` | `--format json` |
| `--verbose` | Show detailed output | false | `--verbose` |
| `--help` | Show help message | - | `--help` |

---

## 📊 Test Coverage

The automated test runner executes the following tests:

### XML Message Tests (7 tests)

1. ✅ Payment Request (pacs.008)
2. ✅ Verification Request (acmt.023)
3. ✅ Transaction Status (pacs.002)
4. ✅ Payment Status - ACSC (Success)
5. ✅ Payment Status - RJCT (Rejection)
6. ✅ Return Request (pacs.004)
7. ✅ Status Request (pacs.028)

### Gateway API Tests (4 tests)

8. ✅ Gateway - Verify Account
9. ✅ Gateway - Payment Request
10. ✅ Gateway - Status Request
11. ✅ Gateway - Return Request

### SomQR API Tests (2 tests)

12. ✅ SomQR - Generate Merchant QR
13. ✅ SomQR - Generate Person QR

**Total:** 13 automated tests

---

## 📁 Report Formats

### JSON Report

**File:** `test-reports/test-report-YYYYMMDD_HHMMSS.json`

```json
{
  "summary": {
    "total": 13,
    "passed": 11,
    "failed": 2,
    "skipped": 0,
    "success_rate": 84.6,
    "duration": 5,
    "start_time": "2024-11-28 15:30:00",
    "end_time": "2024-11-28 15:30:05",
    "base_url": "http://localhost:5000",
    "authentication": true
  },
  "tests": [
    {
      "test": "Payment Request (pacs.008)",
      "status": "passed",
      "http_code": 200,
      "duration": 0.45,
      "tx_id": "ZKBASOS0533212638999283678329850"
    },
    {
      "test": "Gateway - Verify Account",
      "status": "failed",
      "http_code": 403,
      "expected": 200,
      "duration": 0.12
    }
  ]
}
```

### HTML Report

**File:** `test-reports/test-report-YYYYMMDD_HHMMSS.html`

Interactive HTML report with:
- ✅ Visual summary dashboard
- ✅ Color-coded test results
- ✅ Grouped by test category
- ✅ Detailed test information
- ✅ Responsive design

**Open in browser:**
```bash
open test-reports/test-report-*.html
```

---

## 🔧 Usage Examples

### Example 1: Local Development Testing

```bash
# Start your API locally
cd /path/to/SIPS.Connect
dotnet run

# In another terminal, run tests
cd TestScripts
./run-all-tests.sh --skip-auth --verbose
```

### Example 2: CI/CD Integration

```bash
#!/bin/bash
# ci-test.sh

# Run tests and capture exit code
./run-all-tests.sh \
  --url $API_URL \
  --api-key $API_KEY \
  --api-secret $API_SECRET \
  --report-dir ./ci-reports \
  --format json

EXIT_CODE=$?

# Upload reports to artifact storage
aws s3 cp ./ci-reports/ s3://my-bucket/test-reports/ --recursive

# Fail build if tests failed
exit $EXIT_CODE
```

### Example 3: Staging Environment Testing

```bash
./run-all-tests.sh \
  --url https://staging-api.example.com \
  --api-key "staging-key" \
  --api-secret "staging-secret" \
  --report-dir ./staging-reports \
  --format both
```

### Example 4: Production Smoke Tests

```bash
# Run only critical tests
./run-all-tests.sh \
  --url https://api.example.com \
  --api-key "prod-key" \
  --api-secret "prod-secret" \
  --format json
```

---

## 📈 Interpreting Results

### Console Output

```
==========================================
SIPS Connect - Automated Test Runner
==========================================
Base URL: http://localhost:5000
Authentication: Disabled
Report Directory: ./test-reports
Report Format: both
Start Time: 2024-11-28 15:30:00
==========================================

=== XML Message Tests ===

[Test 1] Payment Request (pacs.008)
✓ PASSED (HTTP 200) [0.45s]
  Transaction ID: ZKBASOS0533212638999283678329850

[Test 2] Verification Request (acmt.023)
✗ FAILED (HTTP 403, Expected: 200) [0.12s]

...

==========================================
Test Summary
==========================================
Total Tests:    13
Passed:         11
Failed:         2
Skipped:        0
Success Rate:   84.6%
Total Duration: 5s
End Time:       2024-11-28 15:30:05
==========================================

✓ JSON report saved: ./test-reports/test-report-20241128_153000.json
✓ HTML report saved: ./test-reports/test-report-20241128_153000.html

Reports saved to: ./test-reports
```

### Exit Codes

- **0** - All tests passed
- **1** - One or more tests failed

Use in scripts:
```bash
if ./run-all-tests.sh --skip-auth; then
    echo "All tests passed!"
else
    echo "Tests failed!"
    exit 1
fi
```

---

## 🔍 Troubleshooting

### Issue: All Tests Return 403 Forbidden

**Cause:** API requires authentication

**Solution:**
```bash
# Option 1: Provide credentials
./run-all-tests.sh --api-key "your-key" --api-secret "your-secret"

# Option 2: If endpoint doesn't require auth (development)
./run-all-tests.sh --skip-auth
```

### Issue: Connection Refused

**Cause:** API is not running

**Solution:**
```bash
# Check API is running
./check-api.sh

# Start API
cd .. && dotnet run
```

### Issue: Payload Not Found

**Cause:** Payload files missing

**Solution:**
```bash
# Verify payloads exist
ls -la Payloads/
ls -la Payloads/JSON/

# Re-copy from real-world samples if needed
cp real-world/*.xml Payloads/
```

### Issue: Tests Timeout

**Cause:** API is slow or unresponsive

**Solution:**
```bash
# Check API health
curl -I http://localhost:5000/health

# Check API logs for errors
```

---

## 🔄 CI/CD Integration

### GitHub Actions

```yaml
name: API Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      
      - name: Start API
        run: |
          dotnet run --project SIPS.Connect.csproj &
          sleep 10
      
      - name: Run Tests
        run: |
          cd TestScripts
          chmod +x run-all-tests.sh
          ./run-all-tests.sh --skip-auth --format json
      
      - name: Upload Test Reports
        if: always()
        uses: actions/upload-artifact@v3
        with:
          name: test-reports
          path: TestScripts/test-reports/
      
      - name: Publish Test Results
        if: always()
        uses: dorny/test-reporter@v1
        with:
          name: SIPS Connect Tests
          path: TestScripts/test-reports/*.json
          reporter: json
```

### Azure DevOps

```yaml
steps:
  - task: Bash@3
    displayName: 'Run API Tests'
    inputs:
      targetType: 'filePath'
      filePath: 'TestScripts/run-all-tests.sh'
      arguments: '--url $(API_URL) --api-key $(API_KEY) --api-secret $(API_SECRET) --format json'
  
  - task: PublishTestResults@2
    condition: always()
    inputs:
      testResultsFormat: 'JUnit'
      testResultsFiles: 'TestScripts/test-reports/*.json'
      testRunTitle: 'SIPS Connect API Tests'
  
  - task: PublishBuildArtifacts@1
    condition: always()
    inputs:
      pathToPublish: 'TestScripts/test-reports'
      artifactName: 'test-reports'
```

### Jenkins

```groovy
pipeline {
    agent any
    
    stages {
        stage('Run Tests') {
            steps {
                sh '''
                    cd TestScripts
                    chmod +x run-all-tests.sh
                    ./run-all-tests.sh \
                        --url $API_URL \
                        --api-key $API_KEY \
                        --api-secret $API_SECRET \
                        --format both
                '''
            }
        }
    }
    
    post {
        always {
            publishHTML([
                reportDir: 'TestScripts/test-reports',
                reportFiles: '*.html',
                reportName: 'Test Report'
            ])
            archiveArtifacts artifacts: 'TestScripts/test-reports/*', allowEmptyArchive: true
        }
    }
}
```

---

## 📝 Customization

### Adding New Tests

Edit `run-all-tests.sh` and add your test:

```bash
# For XML tests
run_xml_test "Your Test Name" "/api/endpoint" "$PAYLOADS_DIR/your-payload.xml" 200

# For JSON tests
run_json_test "Your Test Name" "/api/endpoint" "$JSON_PAYLOADS_DIR/your-payload.json" 200
```

### Custom Report Templates

Modify the HTML template in `run-all-tests.sh` starting at line ~300 to customize the report appearance.

### Environment-Specific Configuration

Create environment-specific scripts:

```bash
# test-dev.sh
./run-all-tests.sh \
  --url https://dev-api.example.com \
  --api-key "$DEV_API_KEY" \
  --api-secret "$DEV_API_SECRET" \
  --report-dir ./dev-reports

# test-staging.sh
./run-all-tests.sh \
  --url https://staging-api.example.com \
  --api-key "$STAGING_API_KEY" \
  --api-secret "$STAGING_API_SECRET" \
  --report-dir ./staging-reports
```

---

## 🎯 Best Practices

### 1. Run Tests Before Deployment

```bash
# Pre-deployment check
./run-all-tests.sh --url $STAGING_URL --api-key $KEY --api-secret $SECRET
if [ $? -eq 0 ]; then
    echo "✓ Tests passed - Safe to deploy"
    ./deploy.sh
else
    echo "✗ Tests failed - Deployment aborted"
    exit 1
fi
```

### 2. Schedule Regular Tests

```bash
# crontab entry - run tests every hour
0 * * * * cd /path/to/TestScripts && ./run-all-tests.sh --skip-auth --format json
```

### 3. Monitor Test Trends

```bash
# Keep historical reports
./run-all-tests.sh --report-dir ./reports/$(date +%Y-%m-%d)
```

### 4. Alert on Failures

```bash
#!/bin/bash
./run-all-tests.sh --skip-auth --format json
if [ $? -ne 0 ]; then
    # Send alert
    curl -X POST https://hooks.slack.com/services/YOUR/WEBHOOK/URL \
      -H 'Content-Type: application/json' \
      -d '{"text":"⚠️ SIPS Connect tests failed!"}'
fi
```

---

## 📚 Related Documentation

- **README.md** - Main test scripts documentation
- **test-scenarios.md** - Detailed test scenarios
- **Payloads/README.md** - XML payloads documentation
- **Payloads/JSON/README.md** - JSON payloads documentation
- **PAYLOAD_UPDATE_SUMMARY.md** - Payload update history

---

## 🆘 Support

### Getting Help

1. Check this guide for common issues
2. Review test reports for detailed error information
3. Check API logs: `docker-compose logs sips-connect`
4. Verify payloads are correct
5. Contact SIPS Connect team

### Reporting Issues

When reporting test failures, include:
- Test report (JSON or HTML)
- Console output
- API logs
- Environment details (URL, authentication method)

---

**Happy Testing! 🚀**

*Automated testing ensures quality and reliability of your SIPS Connect integration.*
