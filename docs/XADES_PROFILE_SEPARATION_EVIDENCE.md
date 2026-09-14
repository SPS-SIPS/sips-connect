# XAdES profile-separation evidence

## Source baseline

The IPS vendor behavior was reconstructed from `e60b24a^`, specifically `XmlSignatureGenerator`, `NativeSigner`, and `NativeVerifier`. It was not inferred from the failing error message.

`IPS_VENDOR_LEGACY` preserves:

- two fragment references for `KeyInfo` and `SignedProperties`;
- one anonymous third reference over `document:Document`;
- the historical exclusive-C14N transform declarations;
- the historical inclusive C14N digest and `SignedInfo` implementation;
- the existing IPS ownership and certificate validation path.

`WP_SIPS_PAPSS` preserves the `e60b24a` hardened reference, signature-bound certificate provenance, timestamp, and ownership checks.

## Available message evidence

The checked-in historical fixtures `Packages/SIPS.Core.Tests/TestData/acmt.023.xml`, `pacs.004.xml`, and `pacs.008.xml` each contain an anonymous third reference with exclusive C14N only. They conform structurally to `IPS_VENDOR_LEGACY`.

The available captured ZKBASOS callback log in attachment `55d2cc0e-81c8-4718-b8ed-e0ac3386703c` also contains an anonymous third reference. It was produced by the historical SIPS path and conforms to `IPS_VENDOR_LEGACY`. This explains the reported successful behavior: it predates the global hardened-profile change; there is no alternate current-build validation path that accepts this structure.

No captured ZKBASOS message with an identified FPEnvelope reference is available in the repository or supplied attachments. The `WP_SIPS_PAPSS` identified-envelope form is therefore qualified by the package's hardened generator/verifier cryptographic round trip and negative conformance tests, not represented as captured bank evidence.

## Routing evidence

- Existing Core/SmartVista handlers use the original signer/verifier overloads, which explicitly default to `IpsVendorLegacy`.
- `PapssFacingSipsClient` explicitly signs requests and verifies responses with `WpSipsPapss`.
- `PapssCallbackGuard` explicitly verifies reverse PAPSS traffic with `WpSipsPapss`.
- The PAPSS `.NET 10` `/sips/messages` consumer must call the same explicit `WpSipsPapss` overloads from the shared `net8.0` package.

## Qualification

- XAdES conformance: 13 passed.
- SIPS Connect: 21 passed.
- SIPS Core/SmartVista: 179 passed, 7 skipped.
- Negative coverage distinguishes reference count, missing/non-fragment URI, wrong root target, missing enveloped-signature transform, and incorrect SignedInfo canonicalization.
