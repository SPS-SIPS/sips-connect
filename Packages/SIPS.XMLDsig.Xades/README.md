# SPS SIPS XML Security and XAdES

This package provides XML Digital Signature (XMLDSig) and XML Advanced Electronic Signatures (XAdES) for SIPS.

## Explicit profiles

The package exposes two strongly typed profiles through `XadesProfile`:

- `IpsVendorLegacy` preserves the historical SmartVista/IPS signature contract and is the default for existing overloads.
- `WpSipsPapss` preserves the hardened identified-envelope WP-SIPS contract and must be selected explicitly by PAPSS callers.

```csharp
var ipsSigned = signer.SignEnvelope(xml, XadesProfile.IpsVendorLegacy);
var ipsResult = await verifier.VerifySignature(xml, true, XadesProfile.IpsVendorLegacy, ct);

var papssSigned = signer.SignEnvelope(xml, XadesProfile.WpSipsPapss);
var papssResult = await verifier.VerifyWithProvenance(xml, XadesProfile.WpSipsPapss, ct);
```

No profile is inferred from XML. The original overloads remain compatible and route to `IpsVendorLegacy`. The package targets `net8.0` and can be referenced by the PAPSS `.NET 10` service; that service must use the explicit `WpSipsPapss` overloads at `/sips/messages`.

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
dotnet add package Sps.Sips.XmlSecurity.Xades --version 1.0.2
```

2. Add the following configuration to your `appsettings.json` file:

```json
{
  "Xades": {
    "CertificatePath": "",
    "PrivateKeyPath": "",
    "PrivateKeyPassphrase": "", // Use only if the private key is encrypted with a passphrase
    "ChainPath": "",
    "Algorithms": ["SHA256withRSA"],
    "VerificationWindowMinutes": 100,
    "BIC": "",
    "WithoutPKI": false,
    "BaseDN": ""
  }
}
```

3. Add the following configuration to your `Startup.cs` file:

```csharp
services.AddXades();
```

This package implements the qualified SPS-side WP-SIPS-01 trust profile. PAPSS
consumers must not reuse SPS trust anchors, ownership rules, or profile policy;
only mechanics proven compatible with authoritative PAPSS material are reusable.
