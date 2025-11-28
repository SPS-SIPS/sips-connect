# JSON Test Payloads - Summary

**Date:** November 28, 2024  
**Created By:** System  
**Purpose:** Gateway APIs & CoreBank Callback Testing

---

## ✅ What Was Created

### New Directory Structure

```
TestScripts/Payloads/
├── README.md (updated)
├── JSON/                              ← NEW DIRECTORY
│   ├── README.md                      ← Comprehensive documentation
│   ├── gateway-verify-request.json
│   ├── gateway-payment-request.json
│   ├── gateway-status-request.json
│   ├── gateway-return-request.json
│   ├── corebank-verification-request.json
│   ├── corebank-verification-response-success.json
│   ├── corebank-verification-response-miss.json
│   ├── corebank-payment-request.json
│   ├── corebank-payment-response-success.json
│   ├── corebank-payment-response-failed.json
│   ├── corebank-completion-notification.json
│   ├── corebank-return-request.json
│   ├── corebank-return-response-success.json
│   ├── somqr-merchant-request.json
│   └── somqr-person-request.json
└── [XML files...]
```

---

## 📦 Files Created

### Gateway API Payloads (4 files)

These test **outbound** requests from CoreBank to SIPS Connect:

1. **gateway-verify-request.json** - Account verification
   - Endpoint: `POST /api/v1/gateway/Verify`
   - Purpose: Verify account before payment

2. **gateway-payment-request.json** - Payment initiation
   - Endpoint: `POST /api/v1/gateway/Payment`
   - Purpose: Send payment to another bank

3. **gateway-status-request.json** - Status inquiry
   - Endpoint: `POST /api/v1/gateway/Status`
   - Purpose: Check transaction status

4. **gateway-return-request.json** - Payment return
   - Endpoint: `POST /api/v1/gateway/Return`
   - Purpose: Reverse/return a payment

### CoreBank Callback Payloads (9 files)

These test **inbound** callbacks from SIPS Connect to CoreBank:

5. **corebank-verification-request.json** - Verification callback
   - Simulates: SIPS calling CoreBank to verify account
   - Triggered by: Incoming acmt.023

6. **corebank-verification-response-success.json** - Account found
   - Response: Account exists and active

7. **corebank-verification-response-miss.json** - Account not found
   - Response: Account doesn't exist

8. **corebank-payment-request.json** - Payment callback
   - Simulates: SIPS calling CoreBank to credit account
   - Triggered by: Incoming pacs.008

9. **corebank-payment-response-success.json** - Payment successful
   - Response: Account credited successfully

10. **corebank-payment-response-failed.json** - Payment failed
    - Response: Insufficient funds, account blocked, etc.

11. **corebank-completion-notification.json** - Settlement complete
    - Simulates: SIPS notifying CoreBank of settlement
    - Triggered by: Incoming pacs.002 ACSC

12. **corebank-return-request.json** - Return callback
    - Simulates: SIPS calling CoreBank to process return
    - Triggered by: Incoming pacs.004

13. **corebank-return-response-success.json** - Return processed
    - Response: Funds returned successfully

### SomQR API Payloads (2 files)

14. **somqr-merchant-request.json** - Generate merchant QR
    - Endpoint: `POST /api/v1/somqr/GenerateMerchantQR`
    - Purpose: POS/merchant payment QR code

15. **somqr-person-request.json** - Generate person QR
    - Endpoint: `POST /api/v1/somqr/GeneratePersonQR`
    - Purpose: P2P payment QR code

---

## 🎯 Key Features

### Real-World Data

All payloads use **real transaction data**:
- ✅ Actual bank BICs: ZKBASOS0, AGROSOS0
- ✅ Real account numbers: SO120010201402005007303
- ✅ Production transaction IDs: ZKBASOS0533212638999283678329850
- ✅ Correct field mappings from jsonAdapter.json

### Complete Coverage

| API Category | Endpoints Covered | Payloads |
|--------------|-------------------|----------|
| **Gateway APIs** | 4 endpoints | 4 files |
| **CoreBank Callbacks** | 5 callback types | 9 files |
| **SomQR APIs** | 2 endpoints | 2 files |
| **Total** | **11 endpoints** | **15 files** |

### Field Mappings

All payloads follow the field mappings defined in `/jsonAdapter.json`:

**Example:**
- **Your JSON field:** `"amount": 9.88`
- **Internal field:** `Amount`
- **ISO 20022 XML:** `<IntrBkSttlmAmt Ccy="USD">9.88</IntrBkSttlmAmt>`

---

## 🔧 Usage Examples

### Test Gateway Payment API

```bash
curl -X POST http://localhost:5000/api/v1/gateway/Payment \
  -H "Content-Type: application/json" \
  -H "X-API-KEY: <api-key>" \
  -H "X-API-SECRET: <api-secret>" \
  -d @TestScripts/Payloads/JSON/gateway-payment-request.json
```

### Test Gateway Verify API

```bash
curl -X POST http://localhost:5000/api/v1/gateway/Verify \
  -H "Content-Type: application/json" \
  -H "X-API-KEY: <api-key>" \
  -H "X-API-SECRET: <api-secret>" \
  -d @TestScripts/Payloads/JSON/gateway-verify-request.json
```

### Simulate CoreBank Callback

```bash
# Simulate SIPS calling your CoreBank payment endpoint
curl -X POST http://your-corebank-url/payment \
  -H "Content-Type: application/json" \
  -H "ApiKey: <api-key>" \
  -H "ApiSecret: <api-secret>" \
  -d @TestScripts/Payloads/JSON/corebank-payment-request.json
```

---

## 📊 Authentication Methods

### Gateway APIs (Client → SIPS)

**Option 1: API Keys (Recommended)**
```bash
-H "X-API-KEY: <api-key>"
-H "X-API-SECRET: <api-secret>"
```

**Option 2: JWT Bearer Token**
```bash
-H "Authorization: Bearer <access-token>"
```

### CoreBank Callbacks (SIPS → CoreBank)

**API Keys (Different Casing!)**
```bash
-H "ApiKey: <api-key>"
-H "ApiSecret: <api-secret>"
```

⚠️ **Important:** Note the header name differences:
- Client → SIPS: `X-API-KEY` / `X-API-SECRET`
- SIPS → CoreBank: `ApiKey` / `ApiSecret`

---

## 🔄 Message Flow

### Outbound Payment (Gateway API)

```
CoreBank → SIPS Connect → SVIP
  (JSON)      (XML ISO 20022)
```

1. CoreBank sends JSON to `/api/v1/gateway/Payment`
2. SIPS converts to pacs.008 XML
3. SIPS signs with private key
4. SIPS sends to SVIP
5. SIPS receives pacs.002 response
6. SIPS converts to JSON
7. SIPS returns JSON to CoreBank

### Inbound Payment (Callback)

```
SVIP → SIPS Connect → CoreBank
  (XML ISO 20022)    (JSON)
```

1. SVIP sends signed pacs.008 XML
2. SIPS validates signature
3. SIPS converts to JSON
4. SIPS calls CoreBank `/payment` endpoint
5. CoreBank processes and responds
6. SIPS converts response to pacs.002
7. SIPS signs and sends to SVIP

---

## 📝 Documentation

### Comprehensive README

Created **`Payloads/JSON/README.md`** with:
- ✅ Complete payload inventory
- ✅ Field mapping explanations
- ✅ Testing scenarios
- ✅ Authentication guide
- ✅ Customization instructions
- ✅ Troubleshooting tips
- ✅ Workflow diagrams
- ✅ Response format examples

### Updated Main README

Updated **`Payloads/README.md`** to:
- ✅ Reference JSON subdirectory
- ✅ Show directory structure
- ✅ Clarify XML vs JSON payloads

---

## 🧪 Testing Scenarios

### Scenario 1: Complete Payment Flow

```bash
# 1. Verify account
curl -X POST http://localhost:5000/api/v1/gateway/Verify \
  -H "Content-Type: application/json" \
  -H "X-API-KEY: <api-key>" -H "X-API-SECRET: <api-secret>" \
  -d @JSON/gateway-verify-request.json

# 2. Send payment
curl -X POST http://localhost:5000/api/v1/gateway/Payment \
  -H "Content-Type: application/json" \
  -H "X-API-KEY: <api-key>" -H "X-API-SECRET: <api-secret>" \
  -d @JSON/gateway-payment-request.json

# 3. Check status
curl -X POST http://localhost:5000/api/v1/gateway/Status \
  -H "Content-Type: application/json" \
  -H "X-API-KEY: <api-key>" -H "X-API-SECRET: <api-secret>" \
  -d @JSON/gateway-status-request.json
```

### Scenario 2: CoreBank Integration Testing

Use CoreBank callback payloads to test your endpoints:

```bash
# Test your verification endpoint
curl -X POST http://your-bank/verify \
  -H "Content-Type: application/json" \
  -H "ApiKey: <api-key>" -H "ApiSecret: <api-secret>" \
  -d @JSON/corebank-verification-request.json

# Test your payment endpoint
curl -X POST http://your-bank/payment \
  -H "Content-Type: application/json" \
  -H "ApiKey: <api-key>" -H "ApiSecret: <api-secret>" \
  -d @JSON/corebank-payment-request.json
```

---

## 🎨 Customization

### Change Transaction IDs

```json
{
  "localId": "YOUR-BANK-REF-123",
  "txId": "YOURBANK0" + timestamp
}
```

### Change Amounts

```json
{
  "amount": 100.00,
  "currency": "USD"  // or "SOS"
}
```

### Change Accounts

```json
{
  "drAccount": "YOUR-CUSTOMER-ACCOUNT",
  "crAccount": "BENEFICIARY-ACCOUNT"
}
```

---

## 🔍 Validation

### JSON Validation

```bash
# Validate all JSON files
cd TestScripts/Payloads/JSON
for file in *.json; do
  echo "Validating $file..."
  jq empty "$file" && echo "✅ Valid" || echo "❌ Invalid"
done
```

### Field Mapping Validation

Check that all fields match `/jsonAdapter.json`:

```bash
# Compare payload fields with jsonAdapter.json
jq '.Endpoints.PaymentRequest.FieldMappings[].UserField' jsonAdapter.json
```

---

## 📚 Related Files

### Configuration
- **jsonAdapter.json** - Field mapping definitions
- **test-config.json** - Test configuration (needs update for JSON)

### Documentation
- **Payloads/README.md** - XML payloads documentation
- **Payloads/JSON/README.md** - JSON payloads documentation (NEW)
- **Readme.md** - Main SIPS Connect documentation
- **test-scenarios.md** - Test scenarios

### Test Scripts
- **test-handlers.ps1** - PowerShell test script (XML only)
- **curl-examples.sh** - Bash test script (XML only)
- **postman-collection.json** - Postman collection

---

## ⏭️ Next Steps

### Immediate

1. ✅ **JSON Payloads Created** - 15 files
2. ✅ **Documentation Created** - Comprehensive README
3. ⏳ **Create JSON Test Scripts** - PowerShell/Bash scripts for JSON APIs
4. ⏳ **Update Postman Collection** - Add JSON payload examples
5. ⏳ **Update test-config.json** - Add JSON payload references

### Future Enhancements

1. **Automated Tests** - Create test suite for Gateway APIs
2. **Mock CoreBank** - Create mock server for callback testing
3. **Validation Scripts** - Validate JSON against schemas
4. **Performance Tests** - Load testing for Gateway APIs
5. **Integration Tests** - End-to-end flow testing

---

## 💡 Benefits

### For Developers

✅ **Complete API Coverage** - All Gateway and CoreBank APIs covered  
✅ **Real Data** - Production-like test data  
✅ **Easy Testing** - Copy-paste curl commands  
✅ **Clear Documentation** - Comprehensive guides  

### For QA/Testers

✅ **Ready-to-Use** - No payload creation needed  
✅ **Multiple Scenarios** - Success, failure, edge cases  
✅ **Clear Examples** - Step-by-step testing guides  
✅ **Validation Tools** - JSON validation commands  

### For Integration Partners

✅ **API Examples** - Real request/response examples  
✅ **Field Mappings** - Clear field documentation  
✅ **Authentication Guide** - Both methods documented  
✅ **Callback Simulation** - Test your endpoints easily  

---

## 🆘 Support

### Common Issues

**Issue: 401 Unauthorized**
- Check API key/secret are correct
- Verify header names (X-API-KEY vs ApiKey)

**Issue: 400 Bad Request**
- Validate JSON syntax: `jq . file.json`
- Check field names match jsonAdapter.json

**Issue: Field Not Mapped**
- Review jsonAdapter.json for correct field names
- Check field type (string, double, datetime, bool)

### Getting Help

1. Check **Payloads/JSON/README.md** for detailed documentation
2. Review **jsonAdapter.json** for field mappings
3. Check application logs for error details
4. Contact SIPS Connect team for support

---

**Summary:** Successfully created 15 JSON test payloads covering all Gateway APIs, CoreBank callbacks, and SomQR APIs, with comprehensive documentation and usage examples! 🚀
