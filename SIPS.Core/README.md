# SIPS Core

This repository contains the core functionality of the SIPS project. It is a collection of packages that provide the basic building blocks for the SIPS project. The core packages are:

- `SIPS.Core`: The core package that provides the basic building blocks for the SIPS project.
- `SIPS.Adapter`: The package that provides the json adapter functionality for the SIPS project.
- `SIPS.Emv`: The package that provides the SomQR EMV functionality for the SIPS project.
- `SIPS.ISO20022`: The package that provides the ISO20022 functionality for the SIPS project.
- `SIPS.PostgreSQL`: The package that provides the PostgreSQL functionality for the SIPS project.
- `SIPS.Xades`: The package that provides the XML Dsig Xades-bes functionality for the SIPS project.

## Installation

To install the core packages, you can use the following command:

```bash
dotnet add package SIPS.Core
```

## Usage

To use the core packages, you can add the following configuration to your `appsettings.json` file:

```json
{
  "ConnectionStrings": {
    "db": ""
  },
  "AllowedHosts": "*",
  "Core": {
    "BaseUrl": "",
    "PublicKeysRepUrl": "",
    "LoginEndpoint": "",
    "RefreshEndpoint": "",
    "Username": "",
    "Password": ""
  },
  "Xades": {
    "CertificatePath": "",
    "PrivateKeyPath": "",
    "ChainPath": "",
    "Algorithms": [
      "SHA1withRSA",
      "SHA256withRSA",
      "SHA384withRSA",
      "SHA512withRSA"
    ],
    "VerificationWindowMinutes": 100,
    "BIC": "",
    "WithoutPKI": false,
    "BaseDN": ""
  },
  "ISO20022": {
    "SIPS": "",
    "BIC": "",
    "Agent": "",
    "Verification": "",
    "Transfer": "",
    "Status": "",
    "Return": "",
    "Key": "",
    "Secret": ""
  },
  "Emv": {
    "AcquirerId": "",
    "FIType": "",
    "FIName": "",
    "Version": "",
    "CountryCode": "",
    "Tags": {
      "MerchantIdentifier": 26,
      "AcquirerTag": 1,
      "MerchantIdTag": 44
    }
  }
}
```

Register SIPS Core in your DI container (requires `IConfiguration`):

```csharp
services.AddCore(configuration);
```

### Shared Services

These services are now used internally by handlers and can be injected elsewhere as needed:

- `ISignatureService` (`SIPS.Core/Services/Verification/SignatureService.cs`)
- `IPersistenceGateway` (`SIPS.Core/Services/Persistence/PersistenceGateway.cs`)
- `ICorrelationService` (`SIPS.Core/Services/Correlation/CorrelationService.cs`)
- `ICallbackClient` (`SIPS.Core/Services/Callback/CallbackClient.cs`)
- `IResponseFactory` (`SIPS.Core/Services/Responses/ResponseFactory.cs`)
- `IPaymentRequestParser`, `IPaymentStatusRequestParser` (`SIPS.Core/Services/ISOParsers/`)

Handlers have been refactored to use these services and include correlation IDs in logs.


## Service Registration (DI)

Registering the Core services wires all handlers, parsers, helpers and persistence. See `SIPS.Core/DI.cs` for the full list.

```csharp
// Parsers (stateless)
services.AddSingleton<IPaymentRequestParser, PaymentRequestParser>();
services.AddSingleton<IPaymentStatusRequestParser, PaymentStatusRequestParser>();
services.AddSingleton<IPayeeVerificationRequestParser, PayeeVerificationRequestParser>();
services.AddSingleton<IReturnPaymentRequestParser, ReturnPaymentRequestParser>();

// Shared helpers
services.AddSingleton<ISignatureService, SignatureService>();
services.AddSingleton<ICorrelationService, CorrelationService>();
services.AddSingleton<ICallbackClient, CallbackClient>();
services.AddSingleton<IResponseFactory, ResponseFactory>();

// Persistence
services.AddScoped<IPersistenceGateway, PersistenceGateway>();

// Handlers (request-scoped)
services.AddScoped<IIncomingVerificationHandler, IncomingVerificationHandler>();
services.AddScoped<IIncomingTransactionHandler, IncomingTransactionHandler>();
services.AddScoped<IIncomingTransactionStatusHandler, IncomingTransactionStatusHandler>();
services.AddScoped<IIncomingReturnTransactionHandler, IncomingReturnTransactionHandler>();
services.AddScoped<IOutgoingVerificationHandler, OutgoingVerificationHandler>();
services.AddScoped<IOutgoingTransactionStatusHandler, OutgoingTransactionStatusHandler>();
services.AddScoped<IOutgoingTransactionHandler, OutgoingTransactionHandler>();
services.AddScoped<IOutgoingReturnTransactionHandler, OutgoingReturnTransactionHandler>();
```

## Handler Dependencies

- **IncomingTransactionHandler** uses `ISignatureService`, `IPersistenceGateway`, `ICorrelationService`, `ICallbackClient`, `IResponseFactory`, `IPaymentRequestParser`.
- **IncomingTransactionStatusHandler** uses `ISignatureService`, `IPersistenceGateway`, `ICorrelationService`, `ICallbackClient`, `IResponseFactory`, `IPaymentStatusRequestParser`.
- **IncomingReturnTransactionHandler** uses `ISignatureService`, `IPersistenceGateway`, `ICorrelationService`, `ICallbackClient`, `IReturnPaymentRequestParser`.
- **IncomingVerificationHandler** uses `ISignatureService`, `IPersistenceGateway`, `ICorrelationService`, `ICallbackClient`, `IPayeeVerificationRequestParser`.
- **Outgoing handlers** use `ISignatureService`, `IPersistenceGateway`, and `ICorrelationService`; they communicate with SIPS via `IInterfaceHttpClient.Send4XML(...)` and perform signing/verification using `INativeSigner`/`INativeVerifier`.

## Parser Fixtures and Test Shims

Under `SIPS.Core.Tests/TestData/`:

- `pacs.008.xml` with `PaymentRequestParserShim`
- `pacs.002.xml` with `PaymentStatusRequestParserShim`
- `acmt.023.xml` with `PayeeVerificationRequestParserShim`
- `pacs.004.xml` with `ReturnPaymentRequestParserShim`
- `acmt.024.xml` (additional fixture available)

Shims parse essential fields and provide safe defaults to avoid schema constructor and MinLength constraints during tests. This keeps tests deterministic and independent of full ISO 20022 schema enforcement.

## Correlation IDs

All handlers create a correlation ID using `ICorrelationService.Create(...)` and include it in logs as `[{CorrelationId}]` to trace a flow across callbacks and SIPS requests.

## Lifetimes

- Parsers and helper services are stateless → registered as Singleton.
- `IPersistenceGateway` is Scoped to align with DB-scoped services (avoids captive dependency).
- All handlers are Scoped (per request/operation).

## Running Tests

```bash
dotnet test Packages.sln -c Debug -v minimal
```

