# SIPS XMLDsig Xades

This package provides XML Digital Signature (XMLDSig) and XML Advanced Electronic Signatures (XAdES) for SIPS.

## Overview

The SIPS XMLDSig Xades package provides XML Digital Signature (XMLDSig) and XML Advanced Electronic Signatures (XAdES) for SIPS. The package includes the following features:

- **XMLDSig**: Signs and verifies XML documents.
- **XAdES**: Signs and verifies XML documents with advanced electronic signatures.
- **Key Management**: Manages keys for XMLDSig and XAdES.
- **Certificate Management**: Manages certificates for XMLDSig and XAdES.

## Installation

To install the package, follow these steps:

1. Add the package to your project.

```bash
dotnet add package SIPS.XMLDsig.Xades
```

2. Add the following configuration to your `appsettings.json` file:

```json
{
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
  }
}
```

3. Add the following configuration to your `Startup.cs` file:

```csharp
services.AddXades();
```
