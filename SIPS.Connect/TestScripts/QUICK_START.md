# Quick Start Guide - SIPS API Testing

## Prerequisites

- .NET 8.0 SDK installed
- Your API project with SIPS handlers configured
- Terminal/Command Prompt access

## Step 1: Start Your API

```bash
# Navigate to your API project directory
cd /path/to/your/api/project

# Start the API (adjust command as needed)
dotnet run
# or
dotnet watch
```

**Note:** Keep this terminal open - the API will run here and show logs.

**Default URL:** The scripts assume your API runs on `https://localhost:443`. If your API uses a different URL, pass it as a parameter.

## Step 2: Verify API is Running

Open a **new terminal** and run:

```bash
cd /path/to/TestScripts

# Check API health (uses http://localhost:443 by default)
./check-api.sh

# Or specify a custom URL
./check-api.sh https://localhost:443
```

You should see:

```
✅ API is reachable!
   HTTP Status: 200
```

## Step 3: Run the Tests

```bash
# Basic test run (uses default http://localhost:443)
./curl-examples.sh

# Specify custom URL
./curl-examples.sh https://localhost:443

# With verbose output for debugging
./curl-examples.sh https://localhost:443 --verbose

# With JSON output for CI/CD
./curl-examples.sh https://localhost:443 --json-output

# With retry logic
./curl-examples.sh https://localhost:443 --retry 3

# All options combined
./curl-examples.sh https://localhost:443 --json-output --verbose --retry 3 --timeout 60
```

## Expected Output

```
==========================================
SIPS Handler Test Scripts - Enhanced
==========================================
Base URL: http://localhost:443
Timeout: 30s
Retry Count: 0
JSON Output: false
Verbose: false
Start Time: 2024-11-16 10:50:00
==========================================

=== Core Handler Tests ===

[Test 1] IncomingTransactionHandler (pacs.008)
Endpoint: http://localhost:443/api/v1/incoming
Payload: pacs.008.xml
✓ SUCCESS (HTTP 200) [0.45s]
  Valid XML response

------------------------------------------
...

==========================================
Test Summary
==========================================
Total Tests:    7
Passed:         7
Failed:         0
Success Rate:   100.0%
Total Duration: 1s
==========================================
```

## Troubleshooting

### API Not Reachable (HTTP 000)

1. Make sure your API is running: `dotnet run` or `dotnet watch`
2. Check if the port is in use: `lsof -i :443` (or your port)
3. Verify the base URL matches your API configuration
4. For HTTPS endpoints, the scripts support self-signed certificates

### HTTP 403 Forbidden

This may be expected if:

- The endpoint requires authentication
- The payload needs a valid signature
- CORS settings are blocking the request
- Check your API logs for detailed error messages

### Tests Failing

1. Use verbose mode: `./curl-examples.sh https://localhost:443 --verbose`
2. Check your API logs for error details
3. Verify payload files exist in `Payloads/` directory
4. Ensure handlers are properly registered in your DI container
5. Review `TROUBLESHOOTING.md` for detailed help

## Quick Start Guide - SIPS Handler Testing

## 🚀 Get Started in 5 Minutes

### Step 1: Ensure Your API is Running

```bash
# Start your API project (adjust command as needed)
cd /path/to/your/api/project
dotnet run
```

Your API should be accessible at `http://localhost:443` (or your configured port).

### Step 2: Choose Your Testing Method

#### Option A: Using Bash Script (Mac/Linux)

```bash
cd /path/to/SIPS.Core.Tests/TestScripts
./curl-examples.sh https://localhost:443
```

#### Option B: Using PowerShell (Windows)

```powershell
cd \path\to\SIPS.Core.Tests\TestScripts
.\test-handlers.ps1 -BaseUrl "https://localhost:443"
```

#### Option C: Using cURL Manually

```bash
# Test IncomingTransactionHandler
curl -X POST https://localhost:443/api/incoming/transaction \
  -H "Content-Type: application/xml" \
  -d @Payloads/pacs.008.xml

# Test IncomingPaymentStatusReportHandler
curl -X POST https://localhost:443/api/incoming/payment-status \
  -H "Content-Type: application/xml" \
  -d @Payloads/pacs.002-payment-status.xml
```

#### Option D: Using Postman

1. Open Postman
2. Import `postman-collection.json`
3. Set the `baseUrl` variable to your API endpoint
4. Run the collection or individual requests

### Step 3: Verify Results

**Success Response:**

- HTTP Status: 200 OK
- Content-Type: application/xml
- Body: Signed FPEnvelope with response message

**Check Database:**

```sql
-- View recent transactions
SELECT TxId, Status, Reason, CreatedAt
FROM iso_messages
ORDER BY CreatedAt DESC
LIMIT 10;

-- View status updates
SELECT ims.*, im.TxId
FROM iso_message_statuses ims
JOIN iso_messages im ON ims.ISOMessageId = im.Id
ORDER BY ims.CreatedAt DESC
LIMIT 10;
```

---

## 📋 Handler Endpoints Reference

Adjust these endpoints to match your API routing:

| Handler                            | Endpoint                       | Message Type |
| ---------------------------------- | ------------------------------ | ------------ |
| IncomingTransactionHandler         | `/api/incoming/transaction`    | pacs.008     |
| IncomingVerificationHandler        | `/api/incoming/verification`   | acmt.023     |
| IncomingTransactionStatusHandler   | `/api/incoming/status`         | pacs.002     |
| IncomingPaymentStatusReportHandler | `/api/incoming/payment-status` | pacs.002     |

---

## 🧪 Common Test Scenarios

### 1. Test Successful Payment

```bash
curl -X POST https://localhost:443/api/incoming/payment-status \
  -H "Content-Type: application/xml" \
  -d @Payloads/pacs.002-payment-status-acsc.xml
```

**Expected:** Transaction status updated to Success

### 2. Test Rejected Payment

```bash
curl -X POST https://localhost:443/api/incoming/payment-status \
  -H "Content-Type: application/xml" \
  -d @Payloads/pacs.002-payment-status-rjct.xml
```

**Expected:** Transaction status updated to Failed

### 3. Test Transaction Not Found

```bash
curl -X POST https://localhost:443/api/incoming/payment-status \
  -H "Content-Type: application/xml" \
  -d @Payloads/pacs.002-payment-status-notfound.xml
```

**Expected:** HTTP 404 or admi.002 error response

---

## 🔧 Customizing Test Data

### Update Transaction IDs

Edit the XML files in `Payloads/` to match your test data:

```xml
<!-- Change this -->
<document:OrgnlTxId>TX-TEST-001</document:OrgnlTxId>

<!-- To match your database -->
<document:OrgnlTxId>YOUR-TRANSACTION-ID</document:OrgnlTxId>
```

### Create Test Transactions

Before testing status handlers, ensure transactions exist:

```sql
INSERT INTO iso_messages (TxId, EndToEndId, Status, CreatedAt)
VALUES ('TX-TEST-001', 'E2E-TEST-001', 'Pending', NOW());
```

---

## ❗ Troubleshooting

### Issue: Connection Refused

**Solution:** Ensure your API is running

```bash
# Check if API is running
curl https://localhost:443/health  # or your health endpoint
```

### Issue: 401 Unauthorized

**Solution:** Signature validation failing

- Check public key configuration
- Verify XML signature is valid
- Ensure certificates are not expired

### Issue: 404 Not Found

**Solution:** Endpoint not configured

- Verify API routing configuration
- Check handler registration in DI container
- Confirm endpoint URLs match your API

### Issue: Transaction Not Found

**Solution:** Test data not seeded

```sql
-- Check if transaction exists
SELECT * FROM iso_messages WHERE TxId = 'TX-TEST-001';

-- If not, insert it
INSERT INTO iso_messages (TxId, EndToEndId, Status, CreatedAt)
VALUES ('TX-TEST-001', 'E2E-TEST-001', 'Pending', NOW());
```

---

## 📚 Next Steps

1. **Review Detailed Scenarios:** See `test-scenarios.md` for comprehensive test cases
2. **Customize Payloads:** Edit XML files in `Payloads/` directory
3. **Automate Testing:** Integrate scripts into your CI/CD pipeline
4. **Monitor Logs:** Check application logs for detailed error messages

---

## 🎯 Success Criteria

Your handlers are working correctly if:

- ✅ All test scripts return HTTP 200
- ✅ Responses contain valid signed XML
- ✅ Database shows expected status updates
- ✅ No errors in application logs
- ✅ CoreBank callbacks are invoked (where applicable)

---

## 📞 Need Help?

- See `README.md` for full documentation
- See `test-scenarios.md` for detailed test cases
- Check application logs for error details
- Review unit tests in `../Tests/` for expected behavior

---

**Happy Testing! 🚀**
