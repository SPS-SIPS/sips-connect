# SIPS Connect Platform Documentation

## Table of Contents

- [SIPS Connect Platform Documentation](#sips-connect-platform-documentation)
  - [Table of Contents](#table-of-contents)
  - [Introduction](#introduction)
  - [Features](#features)
  - [Installation](#installation)
  - [Sample `.env` File](#sample-env-file)
  - [Architecture Overview](#architecture-overview)
  - [API Reference](#api-reference)
    - [Transaction Gateway](#transaction-gateway)
      - [POST `/api/v1/gateway/Verify`](#post-apiv1gatewayverify)
      - [POST `/api/v1/gateway/Payment`](#post-apiv1gatewaypayment)
    - [Incoming Messages](#incoming-messages)
      - [POST `/api/v1/incoming`](#post-apiv1incoming)
      - [POST `/api/v1/incoming`](#post-apiv1incoming-1)
    - [SomQR Merchant](#somqr-merchant)
      - [POST `/api/v1/somqr/GenerateMerchantQR`](#post-apiv1somqrgeneratemerchantqr)
      - [Get `/api/v1/somqr/ParseMerchantQR`](#get-apiv1somqrparsemerchantqr)
    - [SomQR Person](#somqr-person)
      - [POST `/api/v1/somqr/GeneratePersonQR`](#post-apiv1somqrgeneratepersonqr)
      - [Get `/api/v1/somqr/ParsePersonQR`](#get-apiv1somqrparsepersonqr)
  - [Authentication \& Authorization](#authentication--authorization)
    - [Bearer Token Authentication](#bearer-token-authentication)
  - [Error Handling](#error-handling)
  - [Security](#security)
  - [Database Management](#database-management)
  - [Integration with SPS Public Key Repository](#integration-with-sps-public-key-repository)
  - [Contact \& Support](#contact--support)

---

## Introduction

The **SIPS Connect Platform** is a robust solution designed to facilitate the seamless sending and receiving of money between the SIPS SVIP system and local banking systems. It achieves this by translating ISO 20022 messages to and from Local Bank JSON APIs, ensuring secure and efficient financial transactions.

## Features

- **Message Translation:** Converts ISO 20022 messages from SIPS to Local Bank JSON API and vice versa.
- **Transaction Signing:** Secures transactions using private key encryption.
- **Transaction Verification:** Validates transactions using public keys integrated with the SPS Public Key Repository.
- **Data Persistence:** Stores all verifications and transactions in a secure database for audit and reference purposes.

## Installation

Docker is required to run the SIPS Connect Platform. To install Docker, follow the instructions provided in the official Docker documentation: [https://docs.docker.com/get-docker/](https://docs.docker.com/get-docker/)

To run the SIPS Connect Platform, follow these steps:

1. Clone the repository:
   ```bash
   git clone https://github.com/SPS-SIPS/SIPS.Connect.git
   ```
2. Navigate to the project directory:
3. Fill in the required environment variables in the `.env` file:
4. Build the Docker image:
   ```bash
   docker-compose build
   ```
5. Run the Docker container:
   ```bash
    docker-compose up -d
   ```
6. Access the SIPS Connect Platform at `http://localhost:8080`
7. To stop the Docker container, run:
   ```bash
   docker-compose down
   ```
8. To view the logs, run:
   ```bash
    docker-compose logs -f
   ```

## Sample `.env` File

```env
Serilog__WriteTo__File__Args__path=/path/to/logs
Serilog__MinimumLevel__Override__Microsoft=Information
Serilog__MinimumLevel__Override__System=Information
Serilog__MinimumLevel__Default=Information
Xades__CertificatePath=/path/to/certs/certificate.pem
Xades__ChainPath=/path/to/certs/chain.pem
Xades__PrivateKeyPath=/path/to/certs/private.key
Xades__Algorithms__0=SHA256withRSA
Xades__Algorithms__1=SHA1withRSA
Xades__Algorithms__2=SHA512withRSA
Xades__VerificationWindowMinutes=1000
Xades__BIC=YourCompanyBIC
Xades__WithoutPKI=false
ConnectionStrings__db="Host=sips-connect-db;Database=SIPS.Connect.DB;Include Error Detail=True;Username=postgres;Password=secure_password;"
Core__BaseUrl=http://SPS-Repository-Url
Core__PublicKeysRepUrl=/v1/Certificates/Download
Core__LoginEndpoint=/v1/auth/login
Core__RefreshEndpoint=/v1/auth/refresh
Core__Username=YourSPS-Repository-Username
Core__Password=YourSPS-Repository-Password

ISO20022__SIPS=http://svip.url
ISO20022__BIC=YourCompanyBIC
ISO20022__Agent=YourCompanyBIC
ISO20022__Verification=http://your-corebank-verification-url
ISO20022__Transfer=http://your-corebank-transfer-url
ISO20022__Status=http://your-corebank-status-url
ISO20022__Return=http://your-corebank-return-url
ISO20022__Key=your-corebank-key
ISO20022__Secret=your-corebank-secret

-- For this check the SOMQR Standard Documentation
Emv__AcquirerId="010006"
Emv__FIType="01"
Emv__FIName="SIT BANK"
Emv__Version="01"
Emv__CountryCode="SO"
Emv__Tags__MerchantIdentifier=26
Emv__Tags__AcquirerTag=1
Emv__Tags__MerchantIdTag=44

POSTGRES_USER=postgres
POSTGRES_PASSWORD=secure_password
POSTGRES_DB=SIPS.Connect.DB

ASPNETCORE_ENVIRONMENT=Development
```

## Architecture Overview

The SIPS Connect Platform acts as an intermediary gateway, handling both outbound and inbound financial transactions. It ensures data integrity and security through cryptographic signing and verification mechanisms while maintaining comprehensive records in its database.

## API Reference

### Transaction Gateway

The Transaction Gateway facilitates the verification and payment processes between the SIPS SVIP and local banking systems.

#### POST `/api/v1/gateway/Verify`

**Description:**  
Verifies transaction requests by processing `VerificationRequest` JSON objects from the provided adapter.

**Request:**

- **Headers:**

  - `Authorization: Bearer {api_key}:{api_secret}`
  - `Content-Type: application/json`

- **Body:**
  ```json
  {
    "VerificationRequest": {
      /* Verification details */
    }
  }
  ```

**Response:**

- **200 OK:**

  ```json
  {
    "VerificationResponse": {
      /* Verification results */
    }
  }
  ```

- **Error Responses:**
  - `400 Bad Request` – Invalid request format or parameters.
  - `401 Unauthorized` – Missing or invalid authentication credentials.
  - `404 Not Found` – Endpoint or resource not found.
  - `500 Internal Server Error` – Server encountered an unexpected condition.

#### POST `/api/v1/gateway/Payment`

**Description:**  
Processes payment requests by handling `PaymentRequest` JSON objects from the provided adapter.

**Request:**

- **Headers:**

  - `Authorization: Bearer {api_key}:{api_secret}`
  - `Content-Type: application/json`

- **Body:**
  ```json
  {
    "PaymentRequest": {
      /* Payment details */
    }
  }
  ```

**Response:**

- **200 OK:**

  ```json
  {
    "PaymentResponse": {
      /* Payment confirmation */
    }
  }
  ```

- **Error Responses:**
  - `400 Bad Request` – Invalid request format or parameters.
  - `401 Unauthorized` – Missing or invalid authentication credentials.
  - `404 Not Found` – Endpoint or resource not found.
  - `500 Internal Server Error` – Server encountered an unexpected condition.

### Incoming Messages

Processes incoming verification and payment messages from the SIPS SVIP system and triggers your callback URLs using the designated JSON adapter properties.

#### POST `/api/v1/incoming`

**Description:**  
processes Received ISO 20022 To `CB_VerificationRequest` JSON objects and forwards them to your API via the designated callback links.
**Request:**

- **Headers:**

  - `Url: {verification_callback_url}` from the Configuration
  - `Authorization: Bearer {api_key}:{api_secret}`
  - `Content-Type: application/json`

- **Body:**
  ```json
  {
    "CB_VerificationRequest": {
      /* Verification details */
    }
  }
  ```

**Response:**

- **200 OK:**

  ```json
  {
    "CB_VerificationResponse": {
      /* Verification results */
    }
  }
  ```

- **Error Responses:**
  - `400 Bad Request` – Invalid request format or parameters.
  - `401 Unauthorized` – Missing or invalid authentication credentials.
  - `404 Not Found` – Endpoint or resource not found.
  - `500 Internal Server Error` – Server encountered an unexpected condition.

#### POST `/api/v1/incoming`

**Description:**  
Processes received ISO 20022 messages into `CB_PaymentRequest` JSON objects and forwards them to your API via the designated callback links.

**Request:**

- **Headers:**

  - `Url: {verification_callback_url}` from the Configuration
  - `Authorization: Bearer {api_key}:{api_secret}`
  - `Content-Type: application/json`

- **Body:**
  ```json
  {
    "CB_PaymentRequest": {
      /* Payment details */
    }
  }
  ```

**Response:**

- **200 OK:**

  ```json
  {
    "CB_PaymentResponse": {
      /* Payment confirmation */
    }
  }
  ```

- **Error Responses:**
  - `400 Bad Request` – Invalid request format or parameters.
  - `401 Unauthorized` – Missing or invalid authentication credentials.
  - `404 Not Found` – Endpoint or resource not found.
  - `500 Internal Server Error` – Server encountered an unexpected condition.

### SomQR Merchant

The SomQR Merchant API provides endpoints for generating and parsing merchant QR codes.

#### POST `/api/v1/somqr/GenerateMerchantQR`

**Description:**
Generates a merchant QR code based on the provided `SomQRMerchantRequest` JSON object.

**Request:**

- **Headers:**

  - `Authorization: Bearer {api_key}:{api_secret}`
  - `Content-Type: application/json`
  - `Accept: application/json`

- **Body:**
  ```json
  {
    "type": 1,
    "method": 1,
    "merchantId": "12345678",
    "merchantCategoryCode": 5814,
    "currencyCode": 706,
    "merchantName": "HAYATRESTAURANTS",
    "merchantCity": "MOGADISHU",
    "postalCode": "00000",
    "storeLabel": "00116789",
    "terminalLabel": "11002"
  }
  ```

**Response:**

- **200 OK:**

  ```json
  {
    "data": "00020101021126400014so.somqr.sSIPS01060100064408123456785204581453037065802SO5916HAYATRESTAURANTS6009MOGADISHU610500000622103080011678907051100263049CAE"
  }
  ```

- **Error Responses:**
- `400 Bad Request` – Invalid request format or parameters.
- `401 Unauthorized` – Missing or invalid authentication credentials.
- `404 Not Found` – Endpoint or resource not found.

#### Get `/api/v1/somqr/ParseMerchantQR`

**Description:**
Parses a merchant QR code and returns the corresponding `MerchantPayload` JSON object.

**Request:**

-- **query:**

- `?code: 00020101021126400014so.somqr.sSIPS01060100064408123456785204581453037065802SO5916HAYATRESTAURANTS6009MOGADISHU610500000622103080011678907051100263049CAE`

- **Headers:**
- `Authorization: Bearer {api_key}:{api_secret}`

**Response:**

- **200 OK:**

  ```json
  {
    "data": {
      "payloadFormatIndicator": "01",
      "pointOfInitializationMethod": "11",
      "merchantAccount": {
        "26": {
          "globalUniqueIdentifier": "so.somqr.sSIPS",
          "paymentNetworkSpecific": {
            "1": "010006",
            "44": "12345678"
          }
        }
      },
      "merchantCategoryCode": 5814,
      "transactionCurrency": 706,
      "transactionAmount": null,
      "tipOrConvenienceIndicator": null,
      "valueOfConvenienceFeeFixed": null,
      "valueOfConvenienceFeePercentage": null,
      "countyCode": "SO",
      "merchantName": "HAYATRESTAURANTS",
      "merchantCity": "MOGADISHU",
      "postalCode": "00000",
      "additionalData": {
        "billNumber": null,
        "mobileNumber": null,
        "storeLabel": "00116789",
        "loyaltyNumber": null,
        "referenceLabel": null,
        "customerLabel": null,
        "terminalLabel": "11002",
        "purposeOfTransaction": null,
        "additionalConsumerDataRequest": null
      },
      "merchantInformation": null,
      "unreservedTemplate": null,
      "crc": "9CAE"
    }
  }
  ```

- **Error Responses:**
- `400 Bad Request` – Invalid request format or parameters.
- `401 Unauthorized` – Missing or invalid authentication credentials.

### SomQR Person

The SomQR Person API provides endpoints for generating and parsing person QR codes.

#### POST `/api/v1/somqr/GeneratePersonQR`

**Description:**
Generates a person QR code based on the provided `SomQRPersonRequest` JSON object.

**Request:**

- **Headers:**

  - `Authorization: Bearer {api_key}:{api_secret}`
  - `Content-Type: application/json`
  - `Accept: application/json`

- **Body:**
  ```json
  {
    "amount": 0,
    "accountName": "Abdulshakur Ahmed Aided",
    "iban": "SO980000220120129383744",
    "currencyCode": "",
    "particulars": "Payment for Test"
  }
  ```

**Response:**

- **200 OK:**

  ```json
  {
    "data": "000202010211022702010308SIT BANK0423SO9800002201201293837440523Abdulshakur Ahmed Aided0716Payment for Test10041234"
  }
  ```

- **Error Responses:**
- `400 Bad Request` – Invalid request format or parameters.
- `401 Unauthorized` – Missing or invalid authentication credentials.

#### Get `/api/v1/somqr/ParsePersonQR`

**Description:**
Parses a person QR code and returns the corresponding `P2PPayload` JSON object.

**Request:**

-- **query:**

- `?code: 000202010211022702010308SIT BANK0423SO9800002201201293837440523Abdulshakur Ahmed Aided0716Payment for Test10041234`

- **Headers:**
- `Authorization: Bearer {api_key}:{api_secret}`
- **Response:**
- **200 OK:**

  ```json
  {
    "data": {
      "payloadFormatIndicator": "02",
      "pointOfInitializationMethod": "11",
      "schemeIdentifier": "01",
      "fiName": "SIT BANK",
      "accountNumber": "SO980000220120129383744",
      "accountName": "Abdulshakur Ahmed Aided",
      "amount": 0,
      "particulars": "Payment for Test",
      "crc": "1234"
    }
  }
  ```

- **Error Responses:**
- `400 Bad Request` – Invalid request format or parameters.
- `401 Unauthorized` – Missing or invalid authentication credentials.

## Authentication & Authorization

### Bearer Token Authentication

All API endpoints require authentication via the `Authorization` header using a Bearer token. The token is constructed from an `api_key` and `api_secret`.

**Token Format:**

```
Bearer api_key:api_secret
```

**Configuration:**

- **Gateway Bearer Token:**

  - `api_key`: `api_key`
  - `api_secret`: `very_strong_secret`
  - **Sample Token:** `Bearer api_key:very_strong_secret`

- **Incoming Gateway Bearer Token:**
  - `api_key`: `api_key`
  - `api_secret`: `very_strong_secret`
  - **Sample Token:** `Bearer api_key:very_strong_secret`

**Token Provisioning:**

- The Bearer token is retrieved from a configuration file or environment variable to ensure secure management of credentials.

## Error Handling

The platform uses standard HTTP status codes to indicate the success or failure of API requests:

- **400 Bad Request:** The request was invalid or cannot be served. This could be due to malformed request syntax or invalid request parameters.
- **401 Unauthorized:** Authentication credentials were missing or invalid.
- **404 Not Found:** The requested resource could not be found.
- **500 Internal Server Error:** An unexpected error occurred on the server side.

Each error response includes a relevant message detailing the nature of the error to aid in troubleshooting.

## Security

- **Transaction Signing:** All transactions are signed using a private key to ensure authenticity and integrity.
- **Transaction Verification:** Signed transactions are verified using a public key retrieved from the SPS Public Key Repository.
- **Data Encryption:** Sensitive data is encrypted both in transit and at rest to protect against unauthorized access.
- **Secure Storage:** Verification records and transaction details are securely stored in the database with appropriate access controls.

## Database Management

All verifications and transactions processed by the SIPS Connect Platform are securely stored in a PostgreSQL database, ensuring a reliable record for auditing, reconciliation, and historical reference. The database schema is designed for high-performance handling of large transactional volumes while maintaining data integrity and security. It also features automatic schema and database generation capabilities for seamless scalability.

## Integration with SPS Public Key Repository

The platform seamlessly integrates with the SPS Public Key Repository to retrieve public keys required for verifying signed transactions. This ensures that only authenticated and authorized transactions are processed, upholding the highest standards of security and trust. Configuration for this module can be managed through system configuration files or environment variables.

## Contact & Support

For further assistance, support, or inquiries related to the SIPS Connect Platform, please contact our support team:

- **Support Portal:** [www.support.sps.so](https://support.sps.so)

---

_This documentation is continuously updated and revised. For the latest information, please refer to the official SIPS Connect Platform documentation portal. Upcoming upgrades, along with a changelog and feature adaptability details, will be shared in this repository soon._
