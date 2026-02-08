# JSON Test Payloads - Gateway & CoreBank APIs

This directory contains **JSON payloads** for testing SIPS Connect Gateway APIs and CoreBank callback simulations. These payloads complement the XML ISO 20022 messages in the parent directory.

---

## 📋 Payload Inventory

### Gateway APIs (Outbound - Bank to SIPS)

These are requests sent **from CoreBank to SIPS Connect** via the Gateway API.

| File | Endpoint | Description | Use Case |
|------|----------|-------------|----------|
| **gateway-verify-request.json** | POST /api/v1/gateway/Verify | Account verification request | Verify account before payment |
| **gateway-payment-request.json** | POST /api/v1/gateway/Payment | Initiate payment request | Send payment to another bank |
| **gateway-status-request.json** | POST /api/v1/gateway/Status | Query payment status | Check transaction status |
| **gateway-return-request.json** | POST /api/v1/gateway/Return | Request payment return | Return/reverse a payment |

### CoreBank Callbacks (Inbound - SIPS to Bank)

These are requests sent **from SIPS Connect to CoreBank** as callbacks.

| File | Callback Type | Description | Use Case |
|------|---------------|-------------|----------|
| **corebank-verification-request.json** | Verification Callback | Verify account in CoreBank | SIPS received acmt.023 |
| **corebank-verification-response-success.json** | Verification Response | Account found | Account exists and active |
| **corebank-verification-response-miss.json** | Verification Response | Account not found | Account doesn't exist |
| **corebank-payment-request.json** | Payment Callback | Credit customer account | SIPS received pacs.008 |
| **corebank-payment-response-success.json** | Payment Response | Payment successful | Account credited |
| **corebank-payment-response-failed.json** | Payment Response | Payment failed | Insufficient funds, etc. |
| **corebank-completion-notification.json** | Completion Callback | Payment settlement complete | SIPS received pacs.002 ACSC |
| **corebank-return-request.json** | Return Callback | Process return request | SIPS received pacs.004 |
| **corebank-return-response-success.json** | Return Response | Return processed | Funds returned |

### SomQR APIs

| File | Endpoint | Description | Use Case |
|------|----------|-------------|----------|
| **somqr-merchant-request.json** | POST /api/v1/somqr/GenerateMerchantQR | Generate merchant QR code | POS/merchant payment |
| **somqr-person-request.json** | POST /api/v1/somqr/GeneratePersonQR | Generate person QR code | P2P payment |

---

## 🔑 Field Mappings

### Gateway Verify Request

```json
{
  "accNo": "305005007605",          // Account number (short format)
  "accType": "ACCT",                 // Account type (ACCT, IBAN, etc.)
  "agent": "AGROSOS0",               // Target bank BIC
  "QRCode": ""                       // Optional QR code
}
```

**Maps to ISO 20022:** acmt.023 (IdentificationVerificationRequest)

### Gateway Payment Request

```json
{
  "agent": "AGROSOS0",               // Creditor agent BIC
  "lclInstrument": "CRTRM",          // Local instrument (CRTRM = Credit Transfer)
  "ctgPurp": "C2CCRT",               // Category purpose (C2C = Customer to Customer)
  "localId": "AGROSOS089538475",     // End-to-end ID (your reference)
  "amount": 9.88,                    // Transaction amount
  "currency": "USD",                 // Currency code (ISO 4217)
  "drName": "SIPS Business",         // Debtor name
  "drAccount": "SO120010...",        // Debtor account (IBAN)
  "drAccountType": "IBAN",           // Debtor account type
  "crName": "SIPS Business",         // Creditor name
  "crAccount": "SO140010...",        // Creditor account (IBAN)
  "crAccountType": "IBAN",           // Creditor account type
  "crAgentBIC": "AGROSOS0",          // Creditor agent BIC
  "crIssuer": "c",                   // Creditor issuer
  "narration": "Send Money"          // Remittance information
}
```

**Maps to ISO 20022:** pacs.008 (FIToFICustomerCreditTransfer)

### CoreBank Payment Request (Callback)

```json
{
  "fromBIC": "ZKBASOS0",             // Sender bank BIC
  "txId": "ZKBASOS0533...",          // SIPS transaction ID
  "endToEndId": "AGROSOS089...",     // End-to-end ID
  "amount": 9.88,                    // Amount to credit
  "currency": "USD",                 // Currency
  "debtorName": "SIPS Business",     // Sender name
  "debtorAccount": "SO120010...",    // Sender account
  "creditorName": "SIPS Business",   // Receiver name (your customer)
  "creditorAccount": "SO140010...",  // Receiver account (your customer)
  "remittanceInformation": "...",    // Payment description
  "date": "2025-11-28T15:06:07...",  // Transaction timestamp
  "bizMsgIdr": "ZKBASOS0533...",     // Business message ID
  "msgDefIdr": "pacs.008.001.10",    // ISO message type
  // ... additional fields
}
```

**Expected Response:**
```json
{
  "endToEndId": "AGROSOS089538475",
  "status": "Success",               // or "Failed"
  "reason": "Payment processed",
  "additionalInfo": "...",
  "acceptanceDate": "2025-11-28...",
  "txId": "ZKBASOS0533..."
}
```

---

## 🧪 Testing Scenarios

### Scenario 1: Complete Payment Flow (Gateway API)

```bash
# 1. Verify account first
curl -X POST http://localhost:5000/api/v1/gateway/Verify \
  -H "Content-Type: application/json" \
  -H "X-API-KEY: your-api-key" \
  -H "X-API-SECRET: your-api-secret" \
  -d @JSON/gateway-verify-request.json

# 2. Send payment
curl -X POST http://localhost:5000/api/v1/gateway/Payment \
  -H "Content-Type: application/json" \
  -H "X-API-KEY: your-api-key" \
  -H "X-API-SECRET: your-api-secret" \
  -d @JSON/gateway-payment-request.json

# 3. Check status
curl -X POST http://localhost:5000/api/v1/gateway/Status \
  -H "Content-Type: application/json" \
  -H "X-API-KEY: your-api-key" \
  -H "X-API-SECRET: your-api-secret" \
  -d @JSON/gateway-status-request.json
```

### Scenario 2: CoreBank Callback Simulation

To test how your CoreBank responds to SIPS callbacks, you can use these payloads to simulate SIPS calling your endpoints:

```bash
# Simulate SIPS calling your verification endpoint
curl -X POST http://your-corebank-url/verify \
  -H "Content-Type: application/json" \
  -H "ApiKey: your-api-key" \
  -H "ApiSecret: your-api-secret" \
  -d @JSON/corebank-verification-request.json

# Simulate SIPS calling your payment endpoint
curl -X POST http://your-corebank-url/payment \
  -H "Content-Type: application/json" \
  -H "ApiKey: your-api-key" \
  -H "ApiSecret: your-api-secret" \
  -d @JSON/corebank-payment-request.json

# Simulate SIPS calling your completion endpoint
curl -X POST http://your-corebank-url/completion \
  -H "Content-Type: application/json" \
  -H "ApiKey: your-api-key" \
  -H "ApiSecret: your-api-secret" \
  -d @JSON/corebank-completion-notification.json
```

### Scenario 3: SomQR Generation

```bash
# Generate merchant QR code
curl -X POST http://localhost:5000/api/v1/somqr/GenerateMerchantQR \
  -H "Content-Type: application/json" \
  -H "X-API-KEY: your-api-key" \
  -H "X-API-SECRET: your-api-secret" \
  -d @JSON/somqr-merchant-request.json

# Generate person QR code
curl -X POST http://localhost:5000/api/v1/somqr/GeneratePersonQR \
  -H "Content-Type: application/json" \
  -H "X-API-KEY: your-api-key" \
  -H "X-API-SECRET: your-api-secret" \
  -d @JSON/somqr-person-request.json
```

---

## 🔧 Customizing Payloads

### Changing Transaction IDs

```json
{
  "localId": "YOUR-UNIQUE-ID-" + timestamp,
  "txId": "YOURBANK" + timestamp
}
```

### Changing Amounts

```json
{
  "amount": 100.00,  // Change to desired amount
  "currency": "USD"  // Or "SOS" for Somali Shilling
}
```

### Changing Accounts

```json
{
  "drAccount": "SO120010201402005007303",  // Your customer's account
  "crAccount": "SO140010202305005007605"   // Beneficiary account
}
```

### Changing Banks

```json
{
  "agent": "YOURBANK0",  // Target bank BIC (8 characters)
  "fromBIC": "YOURBANK0" // Your bank BIC
}
```

---

## 📊 API Authentication

### Gateway APIs (Client Authentication)

Use **one** of these methods:

**Option 1: API Keys (Recommended for testing)**
```bash
-H "X-API-KEY: your-api-key"
-H "X-API-SECRET: your-api-secret"
```

**Option 2: JWT Bearer Token**
```bash
-H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
```

### CoreBank Callbacks (Server Authentication)

SIPS Connect uses these headers when calling your endpoints:

```bash
-H "ApiKey: your-api-key"      # Note: Different casing!
-H "ApiSecret: your-api-secret"
```

**⚠️ Important:** Note the different header casing:
- **Client → SIPS:** `X-API-KEY` / `X-API-SECRET`
- **SIPS → CoreBank:** `ApiKey` / `ApiSecret`

---

## 🔍 Response Formats

### Success Response (Gateway APIs)

```json
{
  "transactionId": "ZKBASOS0533212638999283678329850",
  "status": "Success",
  "localId": "AGROSOS089538475",
  "reason": "Transaction processed successfully",
  "acceptanceDate": "2025-11-28T15:06:07.935+03:00"
}
```

### Error Response

```json
{
  "status": "Failed",
  "errorCode": "MSG_INVALID_ACCOUNT",
  "message": "Invalid account number",
  "details": "Account format does not match IBAN standard",
  "timestamp": "2025-11-28T13:00:00Z",
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "path": "/api/v1/gateway/Payment"
}
```

### Verification Response

```json
{
  "isVerified": true,
  "message": "Account verified successfully",
  "pam": "SO140010202305005007605",
  "type": "IBAN",
  "customer_name": "SIPS Business",
  "account_currency": "USD",
  "requestId": "VER-123456789"
}
```

---

## ⚙️ Configuration

### jsonAdapter.json

The field mappings between internal fields and user-facing fields are defined in `/jsonAdapter.json`. This allows you to customize field names without changing code.

**Example Mapping:**
```json
{
  "Endpoints": {
    "PaymentRequest": {
      "FieldMappings": [
        {
          "InternalField": "Amount",
          "UserField": "amount",
          "Type": "double"
        }
      ]
    }
  }
}
```

This means:
- **Your JSON:** `"amount": 9.88`
- **Internal:** Mapped to `Amount` property
- **ISO 20022:** Converted to `<IntrBkSttlmAmt Ccy="USD">9.88</IntrBkSttlmAmt>`

---

## 🧪 Postman Collection

Import the Postman collection for easier testing:

```bash
# Import this file into Postman
../postman-collection.json
```

The collection includes:
- ✅ All Gateway API endpoints
- ✅ Pre-configured authentication
- ✅ Sample requests with test data
- ✅ Environment variables for easy switching

---

## 📝 Test Data

### Test Banks
- **ZKBASOS0** - Zaad Bank (Sender)
- **AGROSOS0** - Amal Bank (Receiver)

### Test Accounts
- **Debtor:** `SO120010201402005007303` (ZKBASOS0)
- **Creditor:** `SO140010202305005007605` (AGROSOS0)
- **Short Format:** `305005007605` (for verification)

### Test Amounts
- **Small:** `9.88 USD`
- **Medium:** `100.00 USD`
- **Large:** `1000.00 USD`

### Test Transaction IDs
- **BizMsgIdr:** `ZKBASOS0533212638999283678364390`
- **TxId:** `ZKBASOS0533212638999283678329850`
- **EndToEndId:** `AGROSOS089538475`

---

## ⚠️ Important Notes

### Date/Time Format

All timestamps use **ISO 8601** format:
- **UTC:** `2025-11-28T12:06:07.838735Z`
- **Local (EAT):** `2025-11-28T15:06:07.846+03:00`

### Currency Codes

Use **ISO 4217** currency codes:
- `USD` - US Dollar
- `SOS` - Somali Shilling
- `EUR` - Euro

### Account Types

Supported account types:
- `IBAN` - International Bank Account Number
- `ACCT` - Generic account number
- `BBAN` - Basic Bank Account Number

### Status Values

**Transaction Status:**
- `Pending` - Awaiting processing
- `Success` - Completed successfully
- `Failed` - Transaction failed
- `ReadyForReturn` - Awaiting return processing

**ISO Status Codes:**
- `ACSC` - AcceptedSettlementCompleted
- `RJCT` - Rejected
- `PDNG` - Pending

---

## 🔄 Workflow Integration

### Payment Flow

```
┌──────────┐                    ┌──────────────┐                    ┌──────────┐
│ CoreBank │                    │ SIPS Connect │                    │   SVIP   │
└────┬─────┘                    └──────┬───────┘                    └────┬─────┘
     │                                  │                                 │
     │ 1. POST /gateway/Payment (JSON) │                                 │
     │─────────────────────────────────>│                                 │
     │                                  │                                 │
     │                                  │ 2. Convert to pacs.008 (XML)    │
     │                                  │ 3. Sign with private key        │
     │                                  │                                 │
     │                                  │ 4. POST pacs.008 (Signed)       │
     │                                  │─────────────────────────────────>│
     │                                  │                                 │
     │                                  │ 5. pacs.002 Response            │
     │                                  │<─────────────────────────────────│
     │                                  │                                 │
     │ 6. JSON Response                │                                 │
     │<─────────────────────────────────│                                 │
     │                                  │                                 │
```

### Incoming Payment Flow

```
┌──────────┐                    ┌──────────────┐                    ┌──────────┐
│   SVIP   │                    │ SIPS Connect │                    │ CoreBank │
└────┬─────┘                    └──────┬───────┘                    └────┬─────┘
     │                                  │                                 │
     │ 1. POST pacs.008 (XML Signed)   │                                 │
     │─────────────────────────────────>│                                 │
     │                                  │                                 │
     │                                  │ 2. Validate signature           │
     │                                  │ 3. Convert to JSON              │
     │                                  │                                 │
     │                                  │ 4. POST /payment (JSON)         │
     │                                  │─────────────────────────────────>│
     │                                  │                                 │
     │                                  │ 5. JSON Response                │
     │                                  │<─────────────────────────────────│
     │                                  │                                 │
     │                                  │ 6. Convert to pacs.002          │
     │                                  │ 7. Sign response                │
     │                                  │                                 │
     │ 8. pacs.002 Response (Signed)   │                                 │
     │<─────────────────────────────────│                                 │
     │                                  │                                 │
```

---

## 🆘 Troubleshooting

### Issue: 401 Unauthorized

**Cause:** Missing or invalid API credentials

**Solution:**
```bash
# Verify credentials are correct
-H "X-API-KEY: correct-key"
-H "X-API-SECRET: correct-secret"
```

### Issue: 400 Bad Request - Invalid JSON

**Cause:** Malformed JSON payload

**Solution:**
```bash
# Validate JSON
cat gateway-payment-request.json | jq .
```

### Issue: Field Mapping Errors

**Cause:** Field names don't match jsonAdapter.json configuration

**Solution:** Check `jsonAdapter.json` for correct field names

### Issue: CoreBank Callback Not Received

**Cause:** Callback URL not configured or unreachable

**Solution:** Verify callback URL in SIPS Connect configuration

---

## 📚 Related Documentation

- **XML Payloads:** `../README.md` - ISO 20022 XML messages
- **Test Scenarios:** `../../test-scenarios.md` - Detailed test cases
- **API Documentation:** `../../../Readme.md` - Complete API reference
- **Field Mappings:** `../../../jsonAdapter.json` - JSON field configuration

---

## 💡 Tips

1. **Start with Verification:** Always verify accounts before sending payments
2. **Use Correlation IDs:** Track requests end-to-end with unique IDs
3. **Test Error Cases:** Try invalid accounts, insufficient funds, etc.
4. **Monitor Logs:** Check application logs for detailed error messages
5. **Test Idempotency:** Send same request twice to verify duplicate handling
6. **Use Postman:** Import collection for easier testing and debugging

---

**Last Updated:** November 28, 2024  
**Maintained By:** SIPS Connect Team  
**Total Payloads:** 15 JSON files covering all Gateway and CoreBank APIs
