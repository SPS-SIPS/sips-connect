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
    "WithoutPKI": false
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

Add the following configuration to your `Startup.cs` file:

```csharp
services.AddCore();
```
