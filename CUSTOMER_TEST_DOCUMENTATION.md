# SIPS Connect - Test Cases & Security Test Scenarios

**Document Version:** 1.1  
**Date:** November 27, 2024  
**Prepared For:** Vendor Testing & Security Review  
**Status:** Production Ready

---

## Table of Contents

1. [Overview](#overview)
2. [Scope of Testing](#scope-of-testing)
3. [Supported ISO 20022 Versions](#supported-iso-20022-versions)
4. [Message Flow Diagrams](#message-flow-diagrams)
5. [HTTP Headers & Message Format](#http-headers--message-format)
6. [Error Code Schema](#error-code-schema)
7. [PKI & Signing Architecture](#pki--signing-architecture)
8. [Test Environment Setup](#test-environment-setup)
9. [Functional Test Cases](#functional-test-cases)
10. [Negative Functional Tests](#negative-functional-tests)
11. [Security Test Scenarios](#security-test-scenarios)
12. [Performance Test Scenarios](#performance-test-scenarios)
13. [Test Data Matrix](#test-data-matrix)
14. [Test Automation Scripts](#test-automation-scripts)
15. [Logging & Audit Standards](#logging--audit-standards)
16. [Expected Results & Validation](#expected-results--validation)

---

## Overview

SIPS Connect is a secure financial messaging platform that translates ISO 20022 messages between SIPS SVIP and local banking systems. This document provides comprehensive test cases including security test scenarios for vendor validation.

### Key Features Tested

- **Message Translation:** ISO 20022 to/from JSON API
- **Transaction Signing:** Private key encryption (XAdES-BES)
- **Transaction Verification:** Public key validation via SPS PKI
- **Data Protection:** Encryption at rest and in transit
- **Authentication:** API Key and JWT Bearer token support
- **Data Persistence:** PostgreSQL database with audit trails
- **Idempotency:** Duplicate message handling
- **Store-and-Forward:** Retry mechanisms for failed callbacks

---

## Scope of Testing

### In Scope

The following components and behaviors are covered by this test plan:

- ✅ **Message Handlers**
  - IncomingTransactionHandler (pacs.008)
  - IncomingVerificationHandler (acmt.023)
  - IncomingTransactionStatusHandler (pacs.002)
  - IncomingPaymentStatusReportHandler (pacs.002)
- ✅ **Gateway APIs**
  - Verify endpoint
  - Payment endpoint
  - Status endpoint
  - Return endpoint
- ✅ **SomQR APIs**
  - Merchant QR generation/parsing
  - Person QR generation/parsing
- ✅ **Security Features**
  - XML signature validation (XAdES-BES)
  - API authentication (API Keys, JWT)
  - Data encryption at rest
  - TLS/SSL configuration
  - Input validation & injection prevention
- ✅ **PKI & Certificate Management**
  - Certificate chain validation
  - Certificate expiry checks
  - Private key encryption
  - Key rotation procedures
- ✅ **Audit & Logging**
  - Transaction audit trails
  - Security event logging
  - Error logging
- ✅ **Database Operations**
  - Transaction persistence
  - Status updates
  - Idempotency checks

### Out of Scope

The following are **not** covered by this test plan:

- ❌ **SIPS SVIP Internal Behavior** - Testing of SVIP system internals
- ❌ **CoreBank Internal Logic** - Bank's internal posting/validation logic
- ❌ **External Bank Systems** - Third-party bank integrations
- ❌ **Network Infrastructure** - Firewall rules, load balancers (except TLS)
- ❌ **Database Administration** - PostgreSQL tuning, backup/restore
- ❌ **Operating System Security** - OS-level hardening
- ❌ **Container Orchestration** - Kubernetes/Docker Swarm specifics

### Test Boundaries

```
┌─────────────────────────────────────────────────────────────┐
│                    SIPS SVIP (Out of Scope)                 │
└────────────────────────┬────────────────────────────────────┘
                         │ ISO 20022 XML (Signed)
                         ▼
┌─────────────────────────────────────────────────────────────┐
│                  SIPS Connect (IN SCOPE)                    │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐     │
│  │   Handlers   │  │  Gateway API │  │   Security   │     │
│  │  (ISO 20022) │  │  (JSON API)  │  │  (PKI/Auth)  │     │
│  └──────────────┘  └──────────────┘  └──────────────┘     │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐     │
│  │   Database   │  │    Logging   │  │   SomQR API  │     │
│  └──────────────┘  └──────────────┘  └──────────────┘     │
└────────────────────────┬────────────────────────────────────┘
                         │ JSON API (Authenticated)
                         ▼
┌─────────────────────────────────────────────────────────────┐
│              CoreBank System (Out of Scope)                 │
└─────────────────────────────────────────────────────────────┘
```

---

## Supported ISO 20022 Versions

### Message Types & Versions

| Message Type | ISO Version     | Description                        | Handler                            |
| ------------ | --------------- | ---------------------------------- | ---------------------------------- |
| **pacs.008** | pacs.008.001.10 | FIToFICustomerCreditTransfer       | IncomingTransactionHandler         |
| **pacs.002** | pacs.002.001.12 | FIToFIPaymentStatusReport          | IncomingPaymentStatusReportHandler |
| **pacs.002** | pacs.002.001.12 | FIToFIPaymentStatusReport (Status) | IncomingTransactionStatusHandler   |
| **acmt.023** | acmt.023.001.02 | IdentificationVerificationRequest  | IncomingVerificationHandler        |
| **acmt.024** | acmt.024.001.02 | IdentificationVerificationReport   | Response from Verification         |
| **pacs.004** | pacs.004.001.11 | PaymentReturn                      | IncomingReturnHandler              |
| **pacs.028** | pacs.028.001.05 | FIToFIPaymentStatusRequest         | IncomingStatusRequestHandler       |
| **admi.002** | admi.002.001.01 | MessageReject                      | Error Response                     |

### Version Support Policy

- ✅ **Current Versions:** All versions listed above are fully supported
- ⚠️ **Future Versions:** New ISO versions require:
  - Schema validation updates
  - Handler modifications
  - New test cases
  - Regression testing of existing flows
- 🔄 **Backward Compatibility:** Maintained for 2 major versions
- 📋 **Version Detection:** Based on `MsgDefIdr` field in AppHdr

### API Versioning

- **Current API Version:** v1
- **Endpoint Format:** `/api/v1/{resource}`
- **Version Header:** `Accept: application/xml; version=1`
- **Deprecation Policy:** 6 months notice before version sunset

---

## Message Flow Diagrams

### Flow 1: Incoming Payment (pacs.008 → pacs.002)

```
┌──────────┐                    ┌──────────────┐                    ┌──────────┐
│   SVIP   │                    │ SIPS Connect │                    │ CoreBank │
└────┬─────┘                    └──────┬───────┘                    └────┬─────┘
     │                                  │                                 │
     │ 1. POST pacs.008 (Signed)       │                                 │
     │─────────────────────────────────>│                                 │
     │                                  │                                 │
     │                                  │ 2. Validate Signature           │
     │                                  │────────┐                        │
     │                                  │        │                        │
     │                                  │<───────┘                        │
     │                                  │                                 │
     │                                  │ 3. Parse pacs.008               │
     │                                  │────────┐                        │
     │                                  │        │                        │
     │                                  │<───────┘                        │
     │                                  │                                 │
     │                                  │ 4. Save to DB (Status=Pending)  │
     │                                  │────────┐                        │
     │                                  │        │                        │
     │                                  │<───────┘                        │
     │                                  │                                 │
     │                                  │ 5. POST /payment (JSON)         │
     │                                  │─────────────────────────────────>│
     │                                  │                                 │
     │                                  │ 6. Payment Response             │
     │                                  │<─────────────────────────────────│
     │                                  │                                 │
     │                                  │ 7. Update DB Status             │
     │                                  │────────┐                        │
     │                                  │        │                        │
     │                                  │<───────┘                        │
     │                                  │                                 │
     │ 8. pacs.002 Response (Signed)   │                                 │
     │<─────────────────────────────────│                                 │
     │                                  │                                 │
```

### Flow 2: Payment Status Report (pacs.002 ACSC/RJCT)

```
┌──────────┐                    ┌──────────────┐                    ┌──────────┐
│   SVIP   │                    │ SIPS Connect │                    │ CoreBank │
└────┬─────┘                    └──────┬───────┘                    └────┬─────┘
     │                                  │                                 │
     │ 1. POST pacs.002 (ACSC/RJCT)    │                                 │
     │─────────────────────────────────>│                                 │
     │                                  │                                 │
     │                                  │ 2. Validate & Parse             │
     │                                  │────────┐                        │
     │                                  │        │                        │
     │                                  │<───────┘                        │
     │                                  │                                 │
     │                                  │ 3. Lookup Transaction           │
     │                                  │────────┐                        │
     │                                  │        │                        │
     │                                  │<───────┘                        │
     │                                  │                                 │
     │                                  │ 4. Check Idempotency            │
     │                                  │────────┐                        │
     │                                  │        │                        │
     │                                  │<───────┘                        │
     │                                  │                                 │
     │                                  │ 5. POST /completion (if ACSC)   │
     │                                  │─────────────────────────────────>│
     │                                  │                                 │
     │                                  │ 6. Completion Response          │
     │                                  │<─────────────────────────────────│
     │                                  │                                 │
     │                                  │ 7. Update Status (Success/Failed)│
     │                                  │────────┐                        │
     │                                  │        │                        │
     │                                  │<───────┘                        │
     │                                  │                                 │
     │ 8. 200 OK Response              │                                 │
     │<─────────────────────────────────│                                 │
     │                                  │                                 │
```

### Flow 3: Verification Request (acmt.023 → acmt.024)

```
┌──────────┐                    ┌──────────────┐                    ┌──────────┐
│   SVIP   │                    │ SIPS Connect │                    │ CoreBank │
└────┬─────┘                    └──────┬───────┘                    └────┬─────┘
     │                                  │                                 │
     │ 1. POST acmt.023 (Signed)       │                                 │
     │─────────────────────────────────>│                                 │
     │                                  │                                 │
     │                                  │ 2. Validate & Parse             │
     │                                  │────────┐                        │
     │                                  │        │                        │
     │                                  │<───────┘                        │
     │                                  │                                 │
     │                                  │ 3. POST /verify (JSON)          │
     │                                  │─────────────────────────────────>│
     │                                  │                                 │
     │                                  │ 4. Verification Result          │
     │                                  │<─────────────────────────────────│
     │                                  │                                 │
     │                                  │ 5. Build acmt.024               │
     │                                  │────────┐                        │
     │                                  │        │                        │
     │                                  │<───────┘                        │
     │                                  │                                 │
     │ 6. acmt.024 Response (Signed)   │                                 │
     │<─────────────────────────────────│                                 │
     │                                  │                                 │
```

### Flow 4: Gateway Payment (Outbound)

```
┌──────────┐                    ┌──────────────┐                    ┌──────────┐
│ CoreBank │                    │ SIPS Connect │                    │   SVIP   │
└────┬─────┘                    └──────┬───────┘                    └────┬─────┘
     │                                  │                                 │
     │ 1. POST /gateway/Payment (JSON) │                                 │
     │─────────────────────────────────>│                                 │
     │                                  │                                 │
     │                                  │ 2. Validate Auth                │
     │                                  │────────┐                        │
     │                                  │        │                        │
     │                                  │<───────┘                        │
     │                                  │                                 │
     │                                  │ 3. Build pacs.008               │
     │                                  │────────┐                        │
     │                                  │        │                        │
     │                                  │<───────┘                        │
     │                                  │                                 │
     │                                  │ 4. Sign with Private Key        │
     │                                  │────────┐                        │
     │                                  │        │                        │
     │                                  │<───────┘                        │
     │                                  │                                 │
     │                                  │ 5. POST pacs.008 (Signed)       │
     │                                  │─────────────────────────────────>│
     │                                  │                                 │
     │                                  │ 6. pacs.002 Response            │
     │                                  │<─────────────────────────────────│
     │                                  │                                 │
     │                                  │ 7. Parse Response               │
     │                                  │────────┐                        │
     │                                  │        │                        │
     │                                  │<───────┘                        │
     │                                  │                                 │
     │ 8. JSON Response                │                                 │
     │<─────────────────────────────────│                                 │
     │                                  │                                 │
```

---

## HTTP Headers & Message Format

### Required HTTP Headers for Incoming Messages

All incoming ISO 20022 messages must include:

```http
POST /api/v1/incoming HTTP/1.1
Host: sips-connect.example.com
Content-Type: application/xml; charset=utf-8
Accept: application/xml
Content-Length: [length]
User-Agent: [client-identifier]
X-Message-Id: [unique-message-id]
X-Correlation-Id: [correlation-id]
```

### Required HTTP Headers for Gateway APIs

```http
POST /api/v1/gateway/Payment HTTP/1.1
Host: sips-connect.example.com
Content-Type: application/json; charset=utf-8
Accept: application/json
Authorization: Bearer [JWT_TOKEN]
# OR
X-API-KEY: [api-key]
X-API-SECRET: [api-secret]
X-Correlation-Id: [correlation-id]
```

### XML Message Format Requirements

#### Character Encoding

- **Required:** UTF-8 without BOM (Byte Order Mark)
- **Line Endings:** LF (`\n`) or CRLF (`\r\n`) - both accepted
- **Whitespace:** Preserved during signature validation (canonical XML)

#### XML Signature Requirements (XAdES-BES)

```xml
<?xml version="1.0" encoding="UTF-8"?>
<FPEnvelope xmlns="urn:sps:xsd:fps.001.001.01">
  <AppHdr xmlns="urn:iso:std:iso:20022:tech:xsd:head.001.001.02">
    <!-- Application Header -->
  </AppHdr>
  <Document xmlns="urn:iso:std:iso:20022:tech:xsd:pacs.008.001.10">
    <!-- Business Message -->
  </Document>
  <ds:Signature xmlns:ds="http://www.w3.org/2000/09/xmldsig#">
    <ds:SignedInfo>
      <ds:CanonicalizationMethod Algorithm="http://www.w3.org/2001/10/xml-exc-c14n#"/>
      <ds:SignatureMethod Algorithm="http://www.w3.org/2001/04/xmldsig-more#rsa-sha256"/>
      <ds:Reference URI="">
        <ds:Transforms>
          <ds:Transform Algorithm="http://www.w3.org/2000/09/xmldsig#enveloped-signature"/>
          <ds:Transform Algorithm="http://www.w3.org/2001/10/xml-exc-c14n#"/>
        </ds:Transforms>
        <ds:DigestMethod Algorithm="http://www.w3.org/2001/04/xmlenc#sha256"/>
        <ds:DigestValue>[base64-digest]</ds:DigestValue>
      </ds:Reference>
    </ds:SignedInfo>
    <ds:SignatureValue>[base64-signature]</ds:SignatureValue>
    <ds:KeyInfo>
      <ds:X509Data>
        <ds:X509Certificate>[base64-certificate]</ds:X509Certificate>
      </ds:X509Data>
    </ds:KeyInfo>
  </ds:Signature>
</FPEnvelope>
```

#### Canonicalization Rules

- **Algorithm:** Exclusive XML Canonicalization (exc-c14n)
- **Whitespace:** Normalized according to C14N rules
- **Namespace Prefixes:** Preserved
- **Comments:** Removed during canonicalization

#### Signature Validation Notes

- Whitespace **inside** element content affects signature
- Whitespace **between** elements does not affect signature (after canonicalization)
- Line breaks are normalized during canonicalization
- XML declaration (`<?xml...?>`) is not part of signature

### Response Format

#### Success Response (200 OK)

```xml
<?xml version="1.0" encoding="UTF-8"?>
<FPEnvelope xmlns="urn:sps:xsd:fps.001.001.01">
  <AppHdr xmlns="urn:iso:std:iso:20022:tech:xsd:head.001.001.02">
    <Fr>
      <FIId>
        <FinInstnId>
          <BICFI>SIPSSOMOXXXX</BICFI>
        </FinInstnId>
      </FIId>
    </Fr>
    <To>
      <FIId>
        <FinInstnId>
          <BICFI>BANKSOMOXXXX</BICFI>
        </FinInstnId>
      </FIId>
    </To>
    <BizMsgIdr>[unique-business-message-id]</BizMsgIdr>
    <MsgDefIdr>pacs.002.001.12</MsgDefIdr>
    <CreDt>[ISO-8601-timestamp]</CreDt>
  </AppHdr>
  <Document xmlns="urn:iso:std:iso:20022:tech:xsd:pacs.002.001.12">
    <!-- Response Document -->
  </Document>
  <ds:Signature xmlns:ds="http://www.w3.org/2000/09/xmldsig#">
    <!-- Signature -->
  </ds:Signature>
</FPEnvelope>
```

---

## Error Code Schema

### Error Response Structure

All error responses follow this JSON structure for Gateway APIs:

```json
{
  "status": "Failed",
  "errorCode": "ERROR_CATEGORY_SPECIFIC_CODE",
  "message": "Human-readable error message",
  "details": "Additional technical details (optional)",
  "timestamp": "2024-11-27T13:00:00Z",
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "path": "/api/v1/gateway/Payment"
}
```

For ISO 20022 responses, errors are returned as `admi.002` messages.

### Error Code Categories

#### AUTH - Authentication Errors (401)

| Error Code                      | HTTP Status | Description                            | Resolution                   |
| ------------------------------- | ----------- | -------------------------------------- | ---------------------------- |
| `AUTH_MISSING_CREDENTIALS`      | 401         | No authentication credentials provided | Provide API key or JWT token |
| `AUTH_INVALID_API_KEY`          | 401         | Invalid API key                        | Verify API key is correct    |
| `AUTH_INVALID_JWT`              | 401         | Invalid or malformed JWT token         | Obtain new JWT token         |
| `AUTH_EXPIRED_JWT`              | 401         | JWT token has expired                  | Refresh JWT token            |
| `AUTH_INSUFFICIENT_PERMISSIONS` | 403         | User lacks required permissions        | Contact administrator        |

#### SIG - Signature Validation Errors (400/401)

| Error Code                  | HTTP Status | Description                       | Resolution                              |
| --------------------------- | ----------- | --------------------------------- | --------------------------------------- |
| `SIG_MISSING`               | 400         | XML signature is missing          | Add valid XAdES-BES signature           |
| `SIG_INVALID`               | 401         | Signature validation failed       | Verify signature is correctly generated |
| `SIG_CERT_EXPIRED`          | 401         | Signing certificate has expired   | Renew certificate                       |
| `SIG_CERT_REVOKED`          | 401         | Certificate has been revoked      | Obtain new certificate                  |
| `SIG_CERT_NOT_TRUSTED`      | 401         | Certificate not in trust store    | Register certificate with SPS PKI       |
| `SIG_ALGORITHM_UNSUPPORTED` | 400         | Signature algorithm not supported | Use RSA-SHA256                          |

#### XML - XML Processing Errors (400)

| Error Code           | HTTP Status | Description                         | Resolution                        |
| -------------------- | ----------- | ----------------------------------- | --------------------------------- |
| `XML_MALFORMED`      | 400         | XML is not well-formed              | Fix XML syntax errors             |
| `XML_SCHEMA_INVALID` | 400         | XML does not match ISO 20022 schema | Validate against XSD schema       |
| `XML_MISSING_FIELD`  | 400         | Required field is missing           | Add required field                |
| `XML_INVALID_VALUE`  | 400         | Field contains invalid value        | Correct field value               |
| `XML_XXE_DETECTED`   | 400         | XML External Entity attack detected | Remove external entity references |
| `XML_TOO_LARGE`      | 413         | XML payload exceeds size limit      | Reduce payload size (max 10MB)    |

#### MSG - Message Processing Errors (400/404)

| Error Code                | HTTP Status | Description                | Resolution                 |
| ------------------------- | ----------- | -------------------------- | -------------------------- |
| `MSG_UNSUPPORTED_TYPE`    | 400         | Message type not supported | Use supported message type |
| `MSG_UNSUPPORTED_VERSION` | 400         | ISO version not supported  | Use supported ISO version  |
| `MSG_DUPLICATE`           | 409         | Duplicate message ID       | Use unique message ID      |
| `MSG_TX_NOT_FOUND`        | 404         | Transaction not found      | Verify transaction ID      |
| `MSG_INVALID_CURRENCY`    | 400         | Currency code invalid      | Use valid ISO 4217 code    |
| `MSG_INVALID_AMOUNT`      | 400         | Amount format invalid      | Use valid decimal format   |
| `MSG_INVALID_ACCOUNT`     | 400         | Account number invalid     | Verify account format      |

#### COREBANK - CoreBank Integration Errors (502/504)

| Error Code                    | HTTP Status | Description                   | Resolution                         |
| ----------------------------- | ----------- | ----------------------------- | ---------------------------------- |
| `COREBANK_UNAVAILABLE`        | 502         | CoreBank system unavailable   | Retry later, check CoreBank status |
| `COREBANK_TIMEOUT`            | 504         | CoreBank request timed out    | Retry with longer timeout          |
| `COREBANK_REJECTED`           | 400         | CoreBank rejected transaction | Review rejection reason            |
| `COREBANK_INSUFFICIENT_FUNDS` | 400         | Insufficient funds            | Verify account balance             |
| `COREBANK_ACCOUNT_BLOCKED`    | 400         | Account is blocked            | Contact bank                       |

#### INTERNAL - Internal System Errors (500)

| Error Code              | HTTP Status | Description               | Resolution                          |
| ----------------------- | ----------- | ------------------------- | ----------------------------------- |
| `INTERNAL_ERROR`        | 500         | Unexpected internal error | Contact support with correlation ID |
| `INTERNAL_DB_ERROR`     | 500         | Database error            | Contact support                     |
| `INTERNAL_CONFIG_ERROR` | 500         | Configuration error       | Verify system configuration         |

#### RATE - Rate Limiting Errors (429)

| Error Code            | HTTP Status | Description       | Resolution                              |
| --------------------- | ----------- | ----------------- | --------------------------------------- |
| `RATE_LIMIT_EXCEEDED` | 429         | Too many requests | Wait and retry (see Retry-After header) |

### Error Response Examples

#### Example 1: Invalid Signature

```json
{
  "status": "Failed",
  "errorCode": "SIG_INVALID",
  "message": "XML signature validation failed",
  "details": "Signature digest does not match computed digest",
  "timestamp": "2024-11-27T13:00:00Z",
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "path": "/api/v1/incoming"
}
```

#### Example 2: Transaction Not Found

```json
{
  "status": "Failed",
  "errorCode": "MSG_TX_NOT_FOUND",
  "message": "Transaction not found",
  "details": "No transaction found with TxId: TX-NOTFOUND-001",
  "timestamp": "2024-11-27T13:00:00Z",
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "path": "/api/v1/incoming"
}
```

#### Example 3: CoreBank Timeout

```json
{
  "status": "Failed",
  "errorCode": "COREBANK_TIMEOUT",
  "message": "CoreBank request timed out",
  "details": "Request to CoreBank exceeded 30 second timeout",
  "timestamp": "2024-11-27T13:00:00Z",
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "path": "/api/v1/gateway/Payment"
}
```

---

## PKI & Signing Architecture

### Key Management Overview

| Component        | Key Type                 | Key Use                           | Storage                        |
| ---------------- | ------------------------ | --------------------------------- | ------------------------------ |
| **SIPS Connect** | RSA 2048-bit Private Key | Sign outgoing ISO 20022 messages  | Encrypted file with passphrase |
| **SIPS Connect** | X509 Certificate         | Identify SIPS Connect to banks    | Certificate file               |
| **SIPS Connect** | Public Key Store         | Validate incoming bank signatures | SPS Public Key Repository      |
| **Banks**        | RSA 2048-bit Private Key | Sign incoming ISO 20022 messages  | Bank's secure storage          |
| **Banks**        | X509 Certificate         | Registered with SPS PKI           | SPS Certificate Authority      |

### PKI Workflow

```
┌─────────────────────────────────────────────────────────────────┐
│                    SPS Certificate Authority                    │
│  - Issues certificates to banks                                 │
│  - Maintains public key repository                              │
│  - Handles certificate revocation                               │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         │ Certificate Registration
                         │
         ┌───────────────┴───────────────┐
         │                               │
         ▼                               ▼
┌─────────────────┐             ┌─────────────────┐
│  SIPS Connect   │             │   Bank System   │
│                 │             │                 │
│ Private Key     │             │ Private Key     │
│ (Signs outgoing)│             │ (Signs incoming)│
│                 │             │                 │
│ Public Keys     │             │ Certificate     │
│ (Validates)     │             │ (Registered)    │
└────────┬────────┘             └────────┬────────┘
         │                               │
         │  1. Bank signs pacs.008       │
         │<──────────────────────────────│
         │                               │
         │  2. SIPS validates signature  │
         │     using bank's public key   │
         │                               │
         │  3. SIPS signs pacs.002       │
         │──────────────────────────────>│
         │                               │
         │  4. Bank validates signature  │
         │     using SIPS public key     │
         │                               │
```

### Certificate Requirements

#### Certificate Specifications

- **Algorithm:** RSA
- **Key Size:** 2048 bits minimum (4096 bits recommended)
- **Signature Algorithm:** SHA-256 with RSA
- **Validity Period:** Maximum 3 years
- **Key Usage:** Digital Signature, Non-Repudiation
- **Extended Key Usage:** Client Authentication

#### Certificate Subject

```
CN=<BIC-Code>
O=<Organization-Name>
C=SO
```

Example: `CN=BANKSOMOXXXX, O=Example Bank, C=SO`

### Private Key Protection

#### Encryption

- **Algorithm:** AES-256
- **Passphrase:** Minimum 20 characters
- **Storage:** Encrypted file, never plain text
- **Configuration:** Passphrase encrypted in appsettings.json

#### Example Private Key Generation

```bash
# Generate encrypted private key
openssl req -new -newkey rsa:4096 \
  -keyout private.key \
  -out request.csr \
  -subj "/CN=SIPSSOMOXXXX/O=SIPS Connect/C=SO"
# Prompts for passphrase

# Verify key is encrypted
openssl rsa -in private.key -noout
# Should prompt for passphrase
```

### Certificate Validation Process

#### On Message Receipt

1. **Extract Certificate** from `ds:X509Certificate` element
2. **Verify Certificate Chain** against SPS CA root
3. **Check Certificate Expiry** (NotBefore/NotAfter)
4. **Check Revocation Status** (if CRL/OCSP configured)
5. **Validate Signature** using certificate's public key
6. **Verify BIC Match** (certificate CN matches message sender BIC)

#### Validation Queries

```bash
# Validate certificate chain
openssl verify -verbose -CAfile chain.pem certificate.cer

# Check expiry
openssl x509 -in certificate.cer -noout -dates

# Verify modulus matches private key
openssl x509 -noout -modulus -in certificate.cer | openssl md5
openssl rsa -noout -modulus -in private.key | openssl md5
# MD5 hashes must match
```

### Certificate Rotation

#### Rotation Schedule

- **Recommended:** Every 12 months
- **Maximum:** Before expiry (3 years)
- **Overlap Period:** 30 days (old and new certs both valid)

#### Rotation Process

1. Generate new CSR
2. Submit to SPS CA
3. Receive new certificate
4. Configure new certificate in SIPS Connect
5. Test with new certificate
6. Switch to new certificate
7. Notify partners
8. Decommission old certificate after overlap period

**Reference:** See `pki_docs.md` for detailed procedures

---

## Test Environment Setup

### Prerequisites

1. **Docker** (required for containerized deployment)
2. **PostgreSQL** database (v13+)
3. **Valid certificates** for signing/verification
4. **Test data** seeded in database

### Quick Start

```bash
# 1. Clone repository
git clone https://github.com/SPS-SIPS/SIPS.Connect.git
cd SIPS.Connect

# 2. Configure environment
cp .env.example .env
# Edit .env with your configuration

# 3. Create Docker network
docker network create --driver bridge sips-network

# 4. Start services
docker-compose up -d

# 5. Verify health
curl -k https://localhost:443/health
```

### Test Configuration Files

- **TestScripts/test-config.json** - Test configuration
- **TestScripts/Payloads/** - Sample XML payloads
- **TestScripts/test-scenarios.md** - Detailed test scenarios

---

## Functional Test Cases

### 1. IncomingTransactionHandler (pacs.008)

**Purpose:** Process incoming payment requests

#### Test Case 1.1: Successful Payment Request

**Payload:** `Payloads/pacs.008.xml`

**Steps:**

1. Send POST request to `/api/v1/incoming` with pacs.008 XML
2. Handler validates XML signature
3. Parses pacs.008 message
4. Records transaction in database
5. Calls CoreBank payment endpoint
6. Returns signed pacs.002 response

**Expected Results:**

- ✅ HTTP Status: 200 OK
- ✅ Response contains signed FPEnvelope
- ✅ Response contains pacs.002 document
- ✅ Transaction persisted with Status = Pending or Success
- ✅ CoreBank callback invoked

**Database Validation:**

```sql
SELECT * FROM iso_messages WHERE TxId = 'AGROSOS0528910638962089436554484';
-- Should show Status = Success or Pending
```

#### Test Case 1.2: CoreBank Callback Failure

**Setup:** Configure CoreBank endpoint to return error

**Expected Results:**

- ✅ HTTP Status: 200 OK (handler still returns response)
- ✅ Transaction Status = Failed or Pending
- ✅ Error logged in system

---

### 2. IncomingVerificationHandler (acmt.023)

**Purpose:** Process account verification requests

#### Test Case 2.1: Successful Verification

**Payload:** `Payloads/acmt.023.xml`

**Expected Results:**

- ✅ HTTP Status: 200 OK
- ✅ Response contains signed FPEnvelope
- ✅ Response contains acmt.024 document
- ✅ Verification result persisted

#### Test Case 2.2: Verification Service Unavailable

**Expected Results:**

- ✅ HTTP Status: 200 OK or 500
- ✅ Error logged
- ✅ Appropriate error message in response

---

### 3. IncomingTransactionStatusHandler (pacs.002)

**Purpose:** Process transaction status updates

#### Test Case 3.1: Status Update for Existing Transaction

**Payload:** `Payloads/pacs.002-status.xml`

**Prerequisites:**

- Transaction with TxId exists in database
- Transaction Status = Pending

**Expected Results:**

- ✅ HTTP Status: 200 OK
- ✅ Transaction status updated in database
- ✅ ISOMessageStatus record created
- ✅ Response contains acknowledgment

**Database Validation:**

```sql
SELECT * FROM iso_message_statuses
WHERE ISOMessageId = (SELECT Id FROM iso_messages WHERE TxId = 'TX-TEST-001')
ORDER BY CreatedAt DESC LIMIT 1;
```

#### Test Case 3.2: Status for Non-Existent Transaction

**Payload:** `Payloads/pacs.002-payment-status-notfound.xml`

**Expected Results:**

- ✅ HTTP Status: 200 OK (with admi.002) or 404
- ✅ Response indicates transaction not found
- ✅ No database changes

---

### 4. IncomingPaymentStatusReportHandler (pacs.002)

**Purpose:** Process payment status reports with business logic

#### Test Case 4.1: ACSC Status - CoreBank Success

**Payload:** `Payloads/pacs.002-payment-status-acsc.xml`

**Expected Results:**

- ✅ HTTP Status: 200 OK
- ✅ Transaction Status = Success
- ✅ Transaction Reason contains success message
- ✅ CoreBank callback invoked
- ✅ ISOMessageStatus persisted

**Database Validation:**

```sql
SELECT Status, Reason, AdditionalInfo
FROM iso_messages
WHERE TxId = 'TX-ACSC-001';
-- Status should be 'Success'
```

#### Test Case 4.2: RJCT Status - Payment Rejection

**Payload:** `Payloads/pacs.002-payment-status-rjct.xml`

**Expected Results:**

- ✅ HTTP Status: 200 OK
- ✅ Transaction Status = Failed
- ✅ Transaction Reason = "Received rejection confirmation"
- ✅ CoreBank callback NOT invoked
- ✅ Rejection reason captured in AdditionalInfo (e.g., "AM04 - Insufficient funds")

#### Test Case 4.3: Idempotency - Duplicate Processing

**Test:** Send same ACSC message twice

**Expected Results:**

- ✅ HTTP Status: 200 OK for both requests
- ✅ Transaction Status remains Success
- ✅ CoreBank callback NOT invoked second time (idempotency)
- ✅ New ISOMessageStatus record created (audit trail)

---

### 5. Gateway API Tests

#### Test Case 5.1: Verify Endpoint

**Endpoint:** `POST /api/v1/gateway/Verify`

**Authentication:** Bearer token or API Key headers

```bash
curl -X POST https://localhost:443/api/v1/gateway/Verify \
  -H "Authorization: Bearer JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d @verification_request.json
```

**Expected Results:**

- ✅ HTTP Status: 200 OK with valid credentials
- ✅ HTTP Status: 401 Unauthorized without credentials
- ✅ Valid verification response returned

#### Test Case 5.2: Payment Endpoint

**Endpoint:** `POST /api/v1/gateway/Payment`

**Expected Results:**

- ✅ HTTP Status: 200 OK with valid request
- ✅ Payment processed and signed
- ✅ Transaction recorded in database

#### Test Case 5.3: Status Endpoint

**Endpoint:** `POST /api/v1/gateway/status`

**Expected Results:**

- ✅ Returns current transaction status
- ✅ Status matches database records

#### Test Case 5.4: Return Endpoint

**Endpoint:** `POST /api/v1/gateway/return`

**Expected Results:**

- ✅ Return request processed
- ✅ Appropriate response returned

---

### 6. SomQR API Tests

#### Test Case 6.1: Generate Merchant QR

**Endpoint:** `POST /api/v1/somqr/GenerateMerchantQR`

**Expected Results:**

- ✅ Valid QR code generated
- ✅ QR code parseable

#### Test Case 6.2: Generate Person QR

**Endpoint:** `POST /api/v1/somqr/GeneratePersonQR`

**Expected Results:**

- ✅ Valid P2P QR code generated
- ✅ Contains correct account information

---

## Security Test Scenarios

### 1. Authentication & Authorization Tests

#### Security Test 1.1: API Key Authentication

**Test:** Access protected endpoints with/without API keys

**Test Cases:**

```bash
# Valid API key
curl -X POST https://localhost:443/api/v1/gateway/Payment \
  -H "X-API-KEY: valid_key" \
  -H "X-API-SECRET: valid_secret" \
  -H "Content-Type: application/json" \
  -d @payment_request.json

# Invalid API key
curl -X POST https://localhost:443/api/v1/gateway/Payment \
  -H "X-API-KEY: invalid_key" \
  -H "X-API-SECRET: invalid_secret"

# Missing API key
curl -X POST https://localhost:443/api/v1/gateway/Payment
```

**Expected Results:**

- ✅ Valid credentials: HTTP 200 OK
- ✅ Invalid credentials: HTTP 401 Unauthorized
- ✅ Missing credentials: HTTP 401 Unauthorized
- ✅ Error messages do not reveal system details

#### Security Test 1.2: JWT Bearer Token Authentication

**Test:** Access with valid/expired/malformed JWT tokens

**Expected Results:**

- ✅ Valid token: HTTP 200 OK
- ✅ Expired token: HTTP 401 Unauthorized
- ✅ Malformed token: HTTP 401 Unauthorized
- ✅ Token validation enforced

#### Security Test 1.3: Authorization Bypass Attempts

**Test:** Access endpoints without proper authorization

**Expected Results:**

- ✅ All protected endpoints require authentication
- ✅ No authorization bypass possible
- ✅ Proper role-based access control enforced

---

### 2. XML Signature Validation Tests

#### Security Test 2.1: Valid Signature Verification

**Payload:** `Payloads/pacs.008.xml` (properly signed)

**Expected Results:**

- ✅ Signature validated successfully
- ✅ Message processed
- ✅ HTTP 200 OK

#### Security Test 2.2: Invalid Signature Detection

**Test:** Send message with tampered signature

**Expected Results:**

- ✅ HTTP Status: 401 Unauthorized or 400 Bad Request
- ✅ Error message indicates signature failure
- ✅ No database changes
- ✅ Security event logged

#### Security Test 2.3: Missing Signature

**Test:** Send unsigned message

**Expected Results:**

- ✅ Request rejected
- ✅ Appropriate error response
- ✅ No processing occurs

#### Security Test 2.4: Certificate Expiry

**Test:** Use expired certificate for signing

**Expected Results:**

- ✅ Certificate validation fails
- ✅ Request rejected
- ✅ Error logged

---

### 3. Data Protection & Encryption Tests

#### Security Test 3.1: Data Protection Keys Encryption

**Test:** Verify Data Protection keys are encrypted at rest

**Validation:**

```bash
# Check key files contain encrypted content
cat keys/key-*.xml | grep "encryptedSecret"
```

**Expected Results:**

- ✅ Keys encrypted with X509 certificate or DPAPI
- ✅ No plain-text key material in files
- ✅ Warning shown if keys are unencrypted (dev only)
- ✅ Encrypted keys contain `<encryptedSecret>` elements

**Reference:** See `DATA_PROTECTION_KEY_SECURITY.md` for detailed procedures

#### Security Test 3.2: Private Key Protection

**Test:** Verify private keys are encrypted with passphrase

**Validation:**

```bash
# Attempt to read private key without passphrase
openssl rsa -in private.key -noout
# Should prompt for passphrase
```

**Expected Results:**

- ✅ Private key file is encrypted (AES-256)
- ✅ Passphrase required to use key
- ✅ Passphrase stored encrypted in configuration (ENCRYPTED: prefix)
- ✅ No plain-text passphrases in version control

**Reference:** See `pki_docs.md` for certificate generation procedures

#### Security Test 3.3: Database Connection Security

**Test:** Verify database credentials are protected

**Expected Results:**

- ✅ Connection strings encrypted in configuration
- ✅ SSL/TLS enabled for database connections
- ✅ No credentials in logs or error messages
- ✅ Database access restricted by IP/firewall

#### Security Test 3.4: Secrets Management

**Test:** Verify sensitive configuration is encrypted

**Test Cases:**

```bash
# Check appsettings.json for encrypted values
grep "ENCRYPTED:" appsettings.json

# Verify encryption API works
curl -X POST https://localhost:443/api/v1/SecretManagement/encrypt \
  -H "Authorization: Bearer TOKEN" \
  -H "Content-Type: application/json" \
  -d '"test-secret"'
```

**Expected Results:**

- ✅ API secrets encrypted with `ENCRYPTED:` prefix
- ✅ Certificate passwords encrypted
- ✅ Encryption/decryption API available
- ✅ Data Protection keys used for encryption

---

### 4. Input Validation & Injection Tests

#### Security Test 4.1: XML Injection

**Test:** Send malicious XML payloads

**Test Cases:**

```xml
<!-- XXE Attack -->
<!DOCTYPE foo [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
<root>&xxe;</root>

<!-- XML Bomb -->
<!DOCTYPE lolz [
  <!ENTITY lol "lol">
  <!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
]>
<root>&lol2;</root>
```

**Expected Results:**

- ✅ XXE attacks prevented
- ✅ XML bombs detected and rejected
- ✅ Malformed XML rejected with 400 Bad Request
- ✅ No system information leaked in errors

#### Security Test 4.2: SQL Injection

**Test:** Attempt SQL injection via transaction IDs and parameters

**Test Cases:**

```sql
-- Test with malicious TxId
TxId: "TX-001'; DROP TABLE iso_messages; --"
TxId: "TX-001' OR '1'='1"
TxId: "TX-001' UNION SELECT * FROM users--"
```

**Expected Results:**

- ✅ Parameterized queries prevent injection
- ✅ Input sanitized
- ✅ No database errors exposed
- ✅ Malicious input logged

#### Security Test 4.3: Command Injection

**Test:** Attempt OS command injection

**Test Cases:**

```bash
# Test in various fields
AccountNumber: "12345; rm -rf /"
Name: "Test`whoami`"
```

**Expected Results:**

- ✅ No command execution possible
- ✅ Input validation prevents injection
- ✅ System calls properly sanitized

---

### 5. Network Security Tests

#### Security Test 5.1: TLS/SSL Configuration

**Test:** Verify HTTPS configuration

**Test Cases:**

```bash
# Check SSL/TLS version
openssl s_client -connect localhost:443 -tls1_2

# Check cipher suites
nmap --script ssl-enum-ciphers -p 443 localhost

# Verify certificate
openssl s_client -connect localhost:443 -showcerts
```

**Expected Results:**

- ✅ TLS 1.2 or higher enforced
- ✅ Strong cipher suites only
- ✅ Valid SSL certificate
- ✅ HTTP redirects to HTTPS
- ✅ No weak protocols (SSLv3, TLS 1.0, TLS 1.1)

#### Security Test 5.2: CORS Configuration

**Test:** Cross-Origin Resource Sharing policies

**Expected Results:**

- ✅ CORS properly configured
- ✅ Only allowed origins accepted
- ✅ Credentials handling secure

---

### 6. Error Handling & Information Disclosure Tests

#### Security Test 6.1: Error Message Analysis

**Test:** Trigger various error conditions

**Test Cases:**

- Invalid XML format
- Missing required fields
- Database connection failure
- Invalid transaction ID

**Expected Results:**

- ✅ No stack traces in production
- ✅ No database schema information leaked
- ✅ No internal paths exposed
- ✅ Generic error messages to clients
- ✅ Detailed errors logged server-side only

#### Security Test 6.2: Security Headers

**Test:** Verify security headers present

**Test:**

```bash
curl -I https://localhost:443/health
```

**Expected Headers:**

```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
X-XSS-Protection: 1; mode=block
Strict-Transport-Security: max-age=31536000
Content-Security-Policy: default-src 'self'
```

**Expected Results:**

- ✅ All security headers present
- ✅ Headers properly configured
- ✅ No sensitive information in headers

---

### 7. Audit & Logging Tests

#### Security Test 7.1: Security Event Logging

**Test:** Verify security events are logged

**Events to Log:**

- Authentication failures
- Authorization failures
- Signature validation failures
- Invalid input attempts
- Configuration changes
- Administrative actions

**Expected Results:**

- ✅ All security events logged
- ✅ Logs include timestamp, user, action, IP address
- ✅ Logs protected from tampering
- ✅ No sensitive data in logs (passwords, keys, full card numbers)

#### Security Test 7.2: Audit Trail Completeness

**Test:** Verify complete audit trail for transactions

**Database Validation:**

```sql
-- Check transaction audit trail
SELECT im.TxId, im.Status, im.CreatedAt, ims.Status, ims.CreatedAt
FROM iso_messages im
LEFT JOIN iso_message_statuses ims ON im.Id = ims.ISOMessageId
WHERE im.TxId = 'TX-TEST-001'
ORDER BY ims.CreatedAt;
```

**Expected Results:**

- ✅ All transactions recorded
- ✅ Status changes tracked
- ✅ Timestamps accurate
- ✅ Immutable audit records

---

### 8. Certificate & PKI Tests

#### Security Test 8.1: Certificate Validation

**Test:** Verify certificate validation process

**Test Cases:**

```bash
# Validate certificate chain
openssl verify -verbose -CAfile chain.pem certificate.cer

# Check certificate expiry
openssl x509 -in certificate.cer -noout -dates

# Verify modulus consistency
openssl x509 -noout -modulus -in certificate.cer | openssl md5
openssl rsa -noout -modulus -in private.key | openssl md5
# Both MD5 hashes must match
```

**Expected Results:**

- ✅ Certificate chain validated
- ✅ Certificates not expired
- ✅ Modulus matches between cert and key
- ✅ Certificate revocation checked (if applicable)

**Reference:** See `pki_docs.md` for detailed procedures

#### Security Test 8.2: Certificate Rotation

**Test:** Verify certificate rotation process

**Expected Results:**

- ✅ Old certificates gracefully deprecated
- ✅ New certificates activated smoothly
- ✅ No service interruption
- ✅ Rollback procedure available

---

### 9. Denial of Service (DoS) Tests

#### Security Test 9.1: Rate Limiting

**Test:** Send excessive requests

**Test:**

```bash
# Send 1000 requests rapidly
for i in {1..1000}; do
  curl -X POST https://localhost:443/api/v1/incoming \
    -H "Content-Type: application/xml" \
    -d @Payloads/pacs.008.xml &
done
```

**Expected Results:**

- ✅ Rate limiting enforced
- ✅ HTTP 429 Too Many Requests returned
- ✅ Service remains available
- ✅ Legitimate requests not affected

#### Security Test 9.2: Resource Exhaustion

**Test:** Large payload handling

**Test Cases:**

- Very large XML files (>10MB)
- Deeply nested XML structures
- Excessive concurrent connections

**Expected Results:**

- ✅ Request size limits enforced
- ✅ Connection limits enforced
- ✅ Timeouts prevent resource exhaustion
- ✅ Service remains stable

---

## Performance Test Scenarios

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

**Test Script:**

```bash
# Using Apache Bench
ab -n 1000 -c 100 -p Payloads/pacs.008.xml \
   -T "application/xml" \
   https://localhost:443/api/v1/incoming
```

---

## Test Automation Scripts

### Available Test Scripts

#### 1. PowerShell Test Script

**File:** `TestScripts/test-handlers.ps1`

**Usage:**

```powershell
# Basic usage
.\test-handlers.ps1 -BaseUrl "https://localhost:443"

# With all options
.\test-handlers.ps1 -BaseUrl "https://localhost:443" `
  -JsonOutput -VerboseOutput -RetryCount 3 -Timeout 60
```

**Features:**

- ✅ Comprehensive test reporting with statistics
- ✅ JSON output for CI/CD integration
- ✅ Retry logic for failed tests
- ✅ Response validation (XML structure, transaction IDs)
- ✅ Configurable timeouts
- ✅ Color-coded output
- ✅ Exit codes for automation

#### 2. Bash Test Script

**File:** `TestScripts/curl-examples.sh`

**Usage:**

```bash
# Basic usage
./curl-examples.sh https://localhost:443

# With options
./curl-examples.sh https://localhost:443 --json-output --verbose --retry 3
```

#### 3. Health Check Script

**File:** `TestScripts/test-health.sh`

**Usage:**

```bash
./test-health.sh https://localhost:443
```

**Expected Output:**

```
✅ Health check passed - All systems operational
```

---

## Test Data & Payloads

### Available Test Payloads

Located in `TestScripts/Payloads/`:

1. **pacs.008.xml** - Payment request
2. **acmt.023.xml** - Verification request
3. **pacs.002-status.xml** - Transaction status
4. **pacs.002-payment-status.xml** - Payment status report
5. **pacs.002-payment-status-acsc.xml** - Success scenario
6. **pacs.002-payment-status-rjct.xml** - Rejection scenario
7. **pacs.002-payment-status-notfound.xml** - Not found scenario

### Test Data Management

**Creating Test Transactions:**

```sql
INSERT INTO iso_messages (TxId, EndToEndId, BizMsgIdr, MsgDefIdr, MsgId, Status, CreatedAt)
VALUES ('TX-TEST-001', 'E2E-TEST-001', 'MSG-TEST-001', 'pacs.008.001.10', 'MSG-001', 'Pending', NOW());
```

**Cleaning Up Test Data:**

```sql
DELETE FROM iso_message_statuses WHERE ISOMessageId IN (SELECT Id FROM iso_messages WHERE TxId LIKE 'TX-TEST-%');
DELETE FROM transactions WHERE ISOMessageId IN (SELECT Id FROM iso_messages WHERE TxId LIKE 'TX-TEST-%');
DELETE FROM iso_messages WHERE TxId LIKE 'TX-TEST-%';
```

---

## Expected Results & Validation

### Success Criteria

Your implementation passes testing if:

- ✅ All functional tests return HTTP 200
- ✅ Responses contain valid signed XML
- ✅ Database shows expected status updates
- ✅ No errors in application logs
- ✅ CoreBank callbacks invoked correctly
- ✅ All security tests pass
- ✅ Performance meets requirements
- ✅ Audit logs complete and accurate

### Transaction Status Values

- **Pending:** Initial state, awaiting processing
- **Success:** Transaction completed successfully
- **Failed:** Transaction failed (rejected or error)
- **ReadyForReturn:** Awaiting manual return processing

### HTTP Status Codes

- **200 OK:** Request processed successfully
- **400 Bad Request:** Invalid request format
- **401 Unauthorized:** Invalid signature or credentials
- **404 Not Found:** Transaction not found
- **429 Too Many Requests:** Rate limit exceeded
- **500 Internal Server Error:** Handler processing error

### ISO 20022 Status Codes

- **ACSC:** AcceptedSettlementCompleted
- **RJCT:** Rejected
- **PDNG:** Pending

---

## Additional Documentation

### Reference Documents

1. **test-scenarios.md** - Detailed test scenarios (460 lines)
2. **DATA_PROTECTION_KEY_SECURITY.md** - Data protection guide (452 lines)
3. **pki_docs.md** - Certificate management (177 lines)
4. **Readme.md** - Platform documentation (710 lines)
5. **TROUBLESHOOTING.md** - Troubleshooting guide
6. **QUICK_START.md** - Quick start guide

### Support & Contact

For issues or questions:

- **Support Portal:** [www.support.sps.so](https://support.sps.so)
- Review test-scenarios.md for expected behaviors
- Check API logs for detailed error messages
- Verify test-config.json settings

---

## Test Report Template

After running tests, provide this information:

### Test Execution Summary

- **Test Date:** [Date]
- **Tester:** [Name]
- **Environment:** [Dev/Staging/Production]
- **Total Tests:** [Number]
- **Passed:** [Number]
- **Failed:** [Number]
- **Success Rate:** [Percentage]

### Failed Tests

| Test Case | Expected | Actual   | Notes     |
| --------- | -------- | -------- | --------- |
| [Name]    | [Result] | [Result] | [Details] |

### Security Findings

| Severity | Finding       | Status        | Remediation |
| -------- | ------------- | ------------- | ----------- |
| [Level]  | [Description] | [Open/Closed] | [Action]    |

### Performance Results

- **Average Response Time:** [ms]
- **95th Percentile:** [ms]
- **Error Rate:** [%]
- **Throughput:** [requests/second]

---

**End of Test Documentation**

_This document should be used in conjunction with the detailed test scenarios in `TestScripts/test-scenarios.md` and security documentation in `DATA_PROTECTION_KEY_SECURITY.md` and `pki_docs.md`._
