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
      - [POST `/api/v1/gateway/status`](#post-apiv1gatewaystatus)
      - [POST `/api/v1/gateway/return`](#post-apiv1gatewayreturn)
    - [Incoming Messages](#incoming-messages)
      - [POST `/api/v1/incoming`](#post-apiv1incoming)
      - [POST `/api/v1/incoming`](#post-apiv1incoming-1)
      - [POST `/api/v1/incoming`](#post-apiv1incoming-2)
      - [POST `/api/v1/incoming`](#post-apiv1incoming-3)
      - [POST `/api/v1/incoming`](#post-apiv1incoming-4)
    - [SomQR Merchant](#somqr-merchant)
      - [POST `/api/v1/somqr/GenerateMerchantQR`](#post-apiv1somqrgeneratemerchantqr)
      - [GET `/api/v1/somqr/ParseMerchantQR`](#get-apiv1somqrparsemerchantqr)
    - [SomQR Person](#somqr-person)
      - [POST `/api/v1/somqr/GeneratePersonQR`](#post-apiv1somqrgeneratepersonqr)
      - [GET `/api/v1/somqr/ParsePersonQR`](#get-apiv1somqrparsepersonqr)
  - [Authentication \& Authorization](#authentication--authorization)
    - [Bearer Token Authentication](#bearer-token-authentication)
  - [Error Handling](#error-handling)
  - [Security](#security)
  - [Database Management](#database-management)
  - [Integration with SPS Public Key Repository](#integration-with-sps-public-key-repository)
  - [Contact \& Support](#contact--support)

---

## Introduction

The **SIPS Connect Platform** is a robust solution designed to facilitate the seamless sending and receiving of money between the SIPS SVIP system and local banking systems (Core Banking System). It achieves this by translating ISO 20022 messages to and from Local Bank JSON APIs, ensuring secure and efficient financial transactions.

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
   ```bash
   cd SIPS.Connect
   ```
3. Fill in the required environment variables in the `.env` file:
4. Create Docker Network:
   ```bash
   docker network create --driver bridge sips-network
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
Serilog__WriteTo__File__Args__path=/logs/log.log
Serilog__MinimumLevel__Override__Microsoft=Information
Serilog__MinimumLevel__Override__System=Information
Serilog__MinimumLevel__Default=Information

PGUSER=postgres
POSTGRES_PASSWORD=<DB_PASSWORD>
POSTGRES_DB=postgres

ASPNETCORE_ENVIRONMENT=Development

ConnectionStrings__db="Host=sips-connect-db;Database=SIPS.Connect.DB;Include Error Detail=True;Username=postgres;Password=<DB_PASSWORD>;"
KC_DB=postgres
KC_DB_USERNAME=postgres
KC_DB_PASSWORD=<DB_PASSWORD>
KC_DB_URL="jdbc:postgresql://sips-connect-db:5432/postgres"

Keycloak__Realm__Host="idp:8080"
Keycloak__Realm__Protocol="http"
Keycloak__Realm__ValidateIssuer=false
Keycloak__Realm__Name="mgt"
Keycloak__Realm__Audience="sc-api"
Keycloak__Realm__ValidIssuers__0="http://idp:8080"

# all other environment variables can be set using the SIPS Connect Platform UI http://sips-connect-ui:port/config/endpoint

# Certificate and Private Key locations Must be mounted in the container this has to be done in the docker-compose file 
# Currently the docker-compose file mounts the certificates and private key from the host machine to the container
# Review the docker-compose file for the correct path

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

  - `Authorization: Bearer JWT_TOKEN` or `X-API-KEY: {api_key} X-API-SECRET: {api_secret}`
  - `Content-Type: application/json`

- **Body:**
  ```json
  {
      /*jsonAdapter.VerificationRequest*/
  }
  ```

**Response:**

- **200 OK:**

  ```json
  {
    /*jsonAdapter.VerificationResponse*/
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

  - `Authorization: Bearer JWT_TOKEN` or `X-API-KEY: {api_key} X-API-SECRET: {api_secret}`
  - `Content-Type: application/json`

- **Body:**
  ```json
  {
    /*jsonAdapter.PaymentRequest*/
  }
  ```

**Response:**

- **200 OK:**

  ```json
  {
    /*jsonAdapter.PaymentResponse*/
  }
  ```

- **Error Responses:**
  - `400 Bad Request` – Invalid request format or parameters.
  - `401 Unauthorized` – Missing or invalid authentication credentials.
  - `404 Not Found` – Endpoint or resource not found.
  - `500 Internal Server Error` – Server encountered an unexpected condition.

#### POST `/api/v1/gateway/status`

**Description:**  
Processes payment status requests by handling `StatusRequest` JSON objects from the provided adapter.

**Request:**

- **Headers:**

  - `Authorization: Bearer JWT_TOKEN` or `X-API-KEY: {api_key} X-API-SECRET: {api_secret}`
  - `Content-Type: application/json`

- **Body:**
  ```json
  {
    /*jsonAdapter.StatusRequest*/
  }
  ```

**Response:**

- **200 OK:**

  ```json
  {
    /*jsonAdapter.CB_PaymentStatusResponse*/
  }
  ```

- **Error Responses:**
  - `400 Bad Request` – Invalid request format or parameters.
  - `401 Unauthorized` – Missing or invalid authentication credentials.
  - `404 Not Found` – Endpoint or resource not found.
  - `500 Internal Server Error` – Server encountered an unexpected condition.

#### POST `/api/v1/gateway/return`

**Description:**  
Processes payment return requests by handling `ReturnRequest` JSON objects from the provided adapter.

**Request:**

- **Headers:**

  - `Authorization: Bearer JWT_TOKEN` or `X-API-KEY: {api_key} X-API-SECRET: {api_secret}`
  - `Content-Type: application/json`

- **Body:**
  ```json
  {
    /*jsonAdapter.ReturnRequest*/
  }
  ```

**Response:**

- **200 OK:**

  ```json
  {
    /*jsonAdapter.CB_ReturnResponse*/
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
processes Received ISO 20022 (acmt.023) and parses them into `CB_VerificationRequest` JSON objects and forwards them to your API via the designated callback links.
**Request:**

- **Headers:**

  - `Url: {verification_callback_url}` from the Configuration
  - `ApiKey: {api_key}`
  - `ApiSecret: {api_secret}`
  - `Content-Type: application/json`

- **Body:**
  ```json
  {
    /*jsonAdapter.CB_VerificationRequest*/
  }
  ```

**Response:**

- **200 OK:**

  ```json
  {
    /*jsonAdapter.CB_VerificationResponse*/
  }
  ```

- **Error Responses:**
  - `400 Bad Request` – Invalid request format or parameters.
  - `401 Unauthorized` – Missing or invalid authentication credentials.
  - `404 Not Found` – Endpoint or resource not found.
  - `500 Internal Server Error` – Server encountered an unexpected condition.

#### POST `/api/v1/incoming`

**Description:**  
Processes received ISO 20022 (pacs.008) and parses them into `CB_PaymentRequest` JSON objects and forwards them to your API via the designated callback links.

**Request:**

- **Headers:**

  - `Url: {transfer_callback_url}` from the Configuration
  - `ApiKey: {api_key}`
  - `ApiSecret: {api_secret}`
  - `Content-Type: application/json`

- **Body:**
  ```json
  {
    /*jsonAdapter.CB_PaymentRequest*/
  }
  ```

**Response:**

- **200 OK:**

  ```json
  {
    /*jsonAdapter.CB_PaymentResponse*/
  }
  ```

- **Error Responses:**
  - `400 Bad Request` – Invalid request format or parameters.
  - `401 Unauthorized` – Missing or invalid authentication credentials.
  - `404 Not Found` – Endpoint or resource not found.
  - `500 Internal Server Error` – Server encountered an unexpected condition.

#### POST `/api/v1/incoming`

**Description:**  
Processes received ISO 20022 (pacs.028) and parses them into `CB_StatusRequest` JSON objects and forwards them to your API via the designated callback links.

**Request:**

- **Headers:**

  - `Url: {status_callback_url}` from the Configuration
  - `ApiKey: {api_key}`
  - `ApiSecret: {api_secret}`
  - `Content-Type: application/json`

- **Body:**
  ```json
  {
    /*jsonAdapter.CB_StatusRequest*/
  }
  ```

**Response:**

- **200 OK:**

  ```json
  {
    /*jsonAdapter.CB_PaymentStatusResponse*/
  }
  ```

- **Error Responses:**
  - `400 Bad Request` – Invalid request format or parameters.
  - `401 Unauthorized` – Missing or invalid authentication credentials.
  - `404 Not Found` – Endpoint or resource not found.
  - `500 Internal Server Error` – Server encountered an unexpected condition.

#### POST `/api/v1/incoming`

**Description:**  
Processes received ISO 20022 (pacs.004) and parses them into `CB_ReturnRequest` JSON objects and forwards them to your API via the designated callback links.

**Request:**

- **Headers:**

  - `Url: {return_callback_url}` from the Configuration
  - `ApiKey: {api_key}`
  - `ApiSecret: {api_secret}`
  - `Content-Type: application/json`

- **Body:**
  ```json
  {
    /*jsonAdapter.CB_ReturnRequest*/
  }
  ```

**Response:**

- **200 OK:**

  ```json
  {
    /*jsonAdapter.CB_ReturnResponse*/
  }
  ```

- **Error Responses:**
  - `400 Bad Request` – Invalid request format or parameters.
  - `401 Unauthorized` – Missing or invalid authentication credentials.
  - `404 Not Found` – Endpoint or resource not found.
  - `500 Internal Server Error` – Server encountered an unexpected condition.


#### POST `/api/v1/incoming`

**Description:**
Processes received ISO 20022 (pacs.002) and parses them into `CB_CompletionNotification` JSON objects and forwards them to your API via the designated callback links.

**Request:**

- **Headers:**
- `Url: {completion_notification_callback_url}` from the Configuration
- `ApiKey: {api_key}`
- `ApiSecret: {api_secret}`
- `Content-Type: application/json`

- **Body:**
```json
{
  /*jsonAdapter.CB_CompletionNotification*/
}
```

**Response:**

- **200 OK:**

  ```json
  {
    /*jsonAdapter.CB_CompletionNotificationResponse*/
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

  - `Authorization: Bearer JWT_TOKEN` or `X-API-KEY: {api_key} X-API-SECRET: {api_secret}`
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

#### GET `/api/v1/somqr/ParseMerchantQR`

**Description:**
Parses a merchant QR code and returns the corresponding `MerchantPayload` JSON object.

**Request:**

-- **query:**

- `?code: 00020101021126400014so.somqr.sSIPS01060100064408123456785204581453037065802SO5916HAYATRESTAURANTS6009MOGADISHU610500000622103080011678907051100263049CAE`

- **Headers:**
- `Authorization: Bearer JWT_TOKEN` or `X-API-KEY: {api_key} X-API-SECRET: {api_secret}`

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

  - `Authorization: Bearer JWT_TOKEN` or `X-API-KEY: {api_key} X-API-SECRET: {api_secret}`
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

#### GET `/api/v1/somqr/ParsePersonQR`

**Description:**
Parses a person QR code and returns the corresponding `P2PPayload` JSON object.

**Request:**

-- **query:**

- `?code: 000202010211022702010308SIT BANK0423SO9800002201201293837440523Abdulshakur Ahmed Aided0716Payment for Test10041234`

- **Headers:**
- `Authorization: Bearer JWT_TOKEN` or `X-API-KEY: {api_key} X-API-SECRET: {api_secret}`
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

### Client authentication (Gateway and SomQR)

Public API endpoints (e.g., `/api/v1/Gateway/*`, `/api/v1/SomQR/*`) accept either of the following:

- Authorization header (JWT):
  - `Authorization: Bearer <JWT_TOKEN>`

- API key headers:
  - `X-API-KEY: <api_key>`
  - `X-API-SECRET: <api_secret>`

Use one method consistently per request.

### Callback authentication (Orchestrated callbacks)

When SIPS Connect calls your callback URLs (see Incoming behavior), it authenticates using these headers:

- `ApiKey: <api_key>`
- `ApiSecret: <api_secret>`

Note the different casing compared to client requests (`ApiKey/ApiSecret` vs `X-API-KEY/X-API-SECRET`).

### Token provisioning

- JWTs or API credentials are sourced from secure configuration (e.g., environment variables, appsettings).

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
