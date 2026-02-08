# SIPS Connect Testing - Quick Reference Card

## 🚀 Run Tests

```bash
# Basic (no auth)
./run-all-tests.sh --skip-auth

# With authentication
./run-all-tests.sh --api-key "KEY" --api-secret "SECRET"

# Custom URL
./run-all-tests.sh --url https://api.example.com --skip-auth

# Verbose output
./run-all-tests.sh --skip-auth --verbose

# JSON report only
./run-all-tests.sh --skip-auth --format json
```

## 📊 View Reports

```bash
# Open HTML report
open test-reports/test-report-*.html

# View JSON report
cat test-reports/test-report-*.json | jq .

# Check latest results
ls -lt test-reports/
```

## 🔍 Check API Health

```bash
# Health check
./check-api.sh

# Custom URL
./check-api.sh https://localhost:443
```

## 📝 Test Coverage

- **13 Automated Tests**
  - 7 XML Message Tests
  - 4 Gateway API Tests
  - 2 SomQR API Tests

## 📁 Key Files

| File | Purpose |
|------|---------|
| `run-all-tests.sh` | Automated test runner |
| `TEST_RUNNER_GUIDE.md` | Complete documentation |
| `test-reports/` | Generated reports |
| `Payloads/` | XML test data |
| `Payloads/JSON/` | JSON test data |

## 🔧 Common Issues

### 403 Forbidden
```bash
# Add authentication
./run-all-tests.sh --api-key "KEY" --api-secret "SECRET"
```

### Connection Refused
```bash
# Check API is running
./check-api.sh
```

### Payload Not Found
```bash
# Verify payloads exist
ls Payloads/*.xml
ls Payloads/JSON/*.json
```

## 🎯 Exit Codes

- **0** = All tests passed ✅
- **1** = Tests failed ❌

## 📚 Documentation

- `TEST_RUNNER_GUIDE.md` - Full guide
- `AUTOMATED_TESTING_SUMMARY.md` - Implementation summary
- `Payloads/README.md` - XML payloads
- `Payloads/JSON/README.md` - JSON payloads

## 💡 Quick Tips

```bash
# CI/CD integration
./run-all-tests.sh --skip-auth && deploy.sh

# Save reports with date
./run-all-tests.sh --report-dir ./reports/$(date +%Y-%m-%d)

# Test specific environment
./run-all-tests.sh --url $STAGING_URL --api-key $KEY --api-secret $SECRET
```

---

**Need help?** Run `./run-all-tests.sh --help`
