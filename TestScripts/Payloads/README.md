# Test Payloads - Real-World ISO 20022 Messages

This directory contains **real-world** ISO 20022 message samples extracted from actual SIPS Connect transactions. These payloads reflect the exact message structure, namespaces, and data formats used in production.

## � Directory Structure

```
Payloads/
├── README.md                          # This file (XML payloads)
├── JSON/                              # JSON payloads for Gateway APIs
│   ├── README.md                      # JSON payloads documentation
│   ├── gateway-*.json                 # Gateway API requests
│   ├── corebank-*.json                # CoreBank callback payloads
│   └── somqr-*.json                   # SomQR API requests
├── pacs.008.xml                       # Payment request
├── pacs.002-*.xml                     # Payment responses & status
├── pacs.004-*.xml                     # Return messages
├── pacs.028-*.xml                     # Status requests
├── acmt.023.xml                       # Verification request
└── acmt.024-*.xml                     # Verification responses
```

**📝 Note:** This directory contains **XML payloads** for ISO 20022 messages. For **JSON payloads** (Gateway APIs, CoreBank callbacks, SomQR), see the **[JSON/](JSON/)** subdirectory.

---

## �📋 Payload Inventory

### Payment Messages (pacs.008 / pacs.002)

| File                                 | Message Type                 | ISO Version     | Description                                             | Use Case                                |
| ------------------------------------ | ---------------------------- | --------------- | ------------------------------------------------------- | --------------------------------------- |
| **pacs.008.xml**                     | FIToFICustomerCreditTransfer | pacs.008.001.10 | Payment request from ZKBASOS0 to AGROSOS0               | Test IncomingTransactionHandler         |
| **pacs.002-payment-status-acsc.xml** | FIToFIPaymentStatusReport    | pacs.002.001.12 | Payment response - ACSC (Accepted Settlement Completed) | Test successful payment completion      |
| **pacs.002-payment-status-rjct.xml** | FIToFIPaymentStatusReport    | pacs.002.001.12 | Payment response - RJCT (Rejected)                      | Test payment rejection handling         |
| **pacs.002-status.xml**              | FIToFIPaymentStatusReport    | pacs.002.001.12 | Confirmation notification - ACSC                        | Test IncomingPaymentStatusReportHandler |

### Verification Messages (acmt.023 / acmt.024)

| File                     | Message Type                      | ISO Version     | Description                            | Use Case                         |
| ------------------------ | --------------------------------- | --------------- | -------------------------------------- | -------------------------------- |
| **acmt.023.xml**         | IdentificationVerificationRequest | acmt.023.001.03 | Account verification request           | Test IncomingVerificationHandler |
| **acmt.024-success.xml** | IdentificationVerificationReport  | acmt.024.001.03 | Verification response - Success        | Test successful verification     |
| **acmt.024-miss.xml**    | IdentificationVerificationReport  | acmt.024.001.03 | Verification response - Miss/Not Found | Test account not found scenario  |

### Return Messages (pacs.004)

| File                                  | Message Type              | ISO Version     | Description            | Use Case                   |
| ------------------------------------- | ------------------------- | --------------- | ---------------------- | -------------------------- |
| **pacs.004-return-request.xml**       | PaymentReturn             | pacs.004.001.11 | Return payment request | Test IncomingReturnHandler |
| **pacs.004-return-response-acsc.xml** | FIToFIPaymentStatusReport | pacs.002.001.12 | Return response - ACSC | Test successful return     |
| **pacs.004-return-response-rjct.xml** | FIToFIPaymentStatusReport | pacs.002.001.12 | Return response - RJCT | Test rejected return       |

### Status Request Messages (pacs.028)

| File                            | Message Type               | ISO Version     | Description            | Use Case                          |
| ------------------------------- | -------------------------- | --------------- | ---------------------- | --------------------------------- |
| **pacs.028-status-request.xml** | FIToFIPaymentStatusRequest | pacs.028.001.05 | Payment status inquiry | Test IncomingStatusRequestHandler |

### Legacy/Deprecated Files

| File                                     | Status        | Notes                                         |
| ---------------------------------------- | ------------- | --------------------------------------------- |
| **pacs.002-payment-status.xml**          | ⚠️ Deprecated | Use specific ACSC/RJCT variants instead       |
| **pacs.002-payment-status-notfound.xml** | ⚠️ Deprecated | Create by modifying TxId in existing payloads |

---

## 🔑 Key Transaction Identifiers

### Test Banks

- **ZKBASOS0** - Zaad Bank (Debtor/Sender)
- **AGROSOS0** - Amal Bank (Creditor/Receiver)

### Sample Transaction IDs

- **BizMsgIdr:** `ZKBASOS0533212638999283678364390`
- **MsgId:** `ZKBASOS0533212638999283678364430`
- **TxId:** `ZKBASOS0533212638999283678329850`
- **EndToEndId:** `AGROSOS089538475`

### Sample Accounts

- **Debtor Account:** `SO120010201402005007303` (ZKBASOS0)
- **Creditor Account:** `SO140010202305005007605` (AGROSOS0)
- **Verification Account:** `305005007605` (short format)

### Sample Amounts

- **Amount:** `9.88 USD`
- **Currency:** `USD`

---

## 📝 Message Structure

All messages follow this envelope structure:

```xml
<FPEnvelope xmlns:header="urn:iso:std:iso:20022:tech:xsd:head.001.001.03"
  xmlns:document="urn:iso:std:iso:20022:tech:xsd:[message-type]"
  xmlns="urn:iso:std:iso:20022:tech:xsd:[message-category]">

  <header:AppHdr>
    <!-- Application Header with routing info -->
  </header:AppHdr>

  <document:Document>
    <!-- ISO 20022 Business Message -->
  </document:Document>

  <!-- Signature would be added here in production -->
</FPEnvelope>
```

### Namespace Conventions

| Namespace Prefix | Purpose                              | Example                                |
| ---------------- | ------------------------------------ | -------------------------------------- |
| `header:`        | Application Header (head.001.001.03) | Routing, message metadata              |
| `document:`      | Business Document                    | Payment, verification, status messages |
| Default          | FPEnvelope wrapper                   | SIPS-specific envelope                 |

---

## 🧪 Testing Scenarios

### Scenario 1: Complete Payment Flow

```bash
# 1. Send payment request
curl -X POST http://localhost:5000/api/v1/incoming \
  -H "Content-Type: application/xml" \
  -d @pacs.008.xml

# 2. Send payment confirmation (ACSC)
curl -X POST http://localhost:5000/api/v1/incoming \
  -H "Content-Type: application/xml" \
  -d @pacs.002-payment-status-acsc.xml
```

**Expected:**

- Transaction created with Status = Pending
- CoreBank callback invoked
- Status updated to Success after confirmation

### Scenario 2: Payment Rejection

```bash
# 1. Send payment request
curl -X POST http://localhost:5000/api/v1/incoming \
  -H "Content-Type: application/xml" \
  -d @pacs.008.xml

# 2. Send payment rejection (RJCT)
curl -X POST http://localhost:5000/api/v1/incoming \
  -H "Content-Type: application/xml" \
  -d @pacs.002-payment-status-rjct.xml
```

**Expected:**

- Transaction Status = Failed
- CoreBank callback NOT invoked for rejection
- Rejection reason captured

### Scenario 3: Account Verification

```bash
# Send verification request
curl -X POST http://localhost:5000/api/v1/incoming \
  -H "Content-Type: application/xml" \
  -d @acmt.023.xml
```

**Expected:**

- Verification service called
- Response contains account details
- acmt.024 response returned

### Scenario 4: Payment Return

```bash
# Send return request
curl -X POST http://localhost:5000/api/v1/incoming \
  -H "Content-Type: application/xml" \
  -d @pacs.004-return-request.xml
```

**Expected:**

- Return processed
- Original transaction updated
- Return confirmation sent

---

## 🔧 Customizing Payloads

### Changing Transaction IDs

To create unique test transactions, modify these fields:

```xml
<!-- In AppHdr -->
<header:BizMsgIdr>ZKBASOS0[UNIQUE-NUMBER]</header:BizMsgIdr>

<!-- In Document -->
<document:MsgId>ZKBASOS0[UNIQUE-NUMBER]</document:MsgId>
<document:TxId>ZKBASOS0[UNIQUE-NUMBER]</document:TxId>
<document:EndToEndId>AGROSOS0[UNIQUE-NUMBER]</document:EndToEndId>
```

### Changing Amounts

```xml
<document:IntrBkSttlmAmt Ccy="USD">100.00</document:IntrBkSttlmAmt>
<document:InstdAmt Ccy="USD">100.00</document:InstdAmt>
```

### Changing Accounts

```xml
<!-- Debtor Account -->
<document:Id>SO120010201402005007303</document:Id>

<!-- Creditor Account -->
<document:Id>SO140010202305005007605</document:Id>
```

### Changing Banks

```xml
<!-- Sender Bank -->
<header:Id>ZKBASOS0</header:Id>

<!-- Receiver Bank -->
<header:Id>AGROSOS0</header:Id>
```

---

## ⚠️ Important Notes

### Signature Requirements

**Production:** All messages MUST include XML signatures (XAdES-BES)

**Testing:** Signatures can be omitted for local testing if signature validation is disabled

### Timestamp Format

All timestamps use ISO 8601 format:

- **UTC:** `2025-11-28T12:06:07.838735Z`
- **Local (EAT):** `2025-11-28T15:06:07.846+03:00`

### Character Encoding

- **Required:** UTF-8 without BOM
- **Line Endings:** LF (`\n`) or CRLF (`\r\n`) both accepted

### Message Size Limits

- **Maximum:** 10 MB per message
- **Typical:** 5-10 KB for payment messages

---

## 🔍 Validation

### XML Schema Validation

```bash
# Validate against ISO 20022 schema
xmllint --noout --schema pacs.008.001.10.xsd pacs.008.xml
```

### Well-Formedness Check

```bash
# Check XML is well-formed
xmllint --noout pacs.008.xml
```

### Namespace Verification

Ensure all namespaces are correctly declared:

- `urn:iso:std:iso:20022:tech:xsd:head.001.001.03`
- `urn:iso:std:iso:20022:tech:xsd:pacs.008.001.10`
- `urn:iso:std:iso:20022:tech:xsd:pacs.002.001.12`
- `urn:iso:std:iso:20022:tech:xsd:acmt.023.001.03`
- `urn:iso:std:iso:20022:tech:xsd:acmt.024.001.03`

---

## 📚 Related Documentation

- **Test Scenarios:** `../test-scenarios.md` - Detailed test cases
- **Test Scripts:** `../test-handlers.ps1` - Automated test runner
- **API Documentation:** `../../Readme.md` - SIPS Connect API reference
- **PKI Documentation:** `../../pki_docs.md` - Certificate management

---

## 🔄 Payload Update History

| Date       | Version | Changes                                          |
| ---------- | ------- | ------------------------------------------------ |
| 2024-11-28 | 2.0     | Updated with real-world messages from production |
| 2024-11-27 | 1.0     | Initial test payloads                            |

---

## 💡 Tips for Testing

1. **Start Simple:** Test with pacs.008.xml first
2. **Check Logs:** Monitor application logs for detailed error messages
3. **Verify Database:** Check `iso_messages` table after each test
4. **Use Correlation IDs:** Track messages end-to-end
5. **Test Idempotency:** Send same message twice to verify duplicate handling
6. **Test Error Cases:** Modify payloads to trigger validation errors

---

## 🆘 Troubleshooting

### Issue: Signature Validation Failed

**Solution:** For local testing, disable signature validation or add test certificates

### Issue: Transaction Not Found

**Solution:** Ensure transaction exists in database before sending status updates

### Issue: Invalid XML

**Solution:** Validate XML structure and namespaces using xmllint

### Issue: Wrong Message Type

**Solution:** Verify `MsgDefIdr` matches the document namespace

---

**Last Updated:** November 28, 2024  
**Maintained By:** SIPS Connect Team  
**Source:** Real-world production messages
