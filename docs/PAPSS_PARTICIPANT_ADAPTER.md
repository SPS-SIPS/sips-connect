# SIPS Connect PAPSS participant adapter

## Contract and compatibility

The existing authenticated JSON endpoints remain unchanged:

- `POST /api/v1/Gateway/Verify`
- `POST /api/v1/Gateway/Payment`
- `POST /api/v1/Gateway/Status`
- `POST /api/v1/Gateway/Return`

An omitted `rail` continues to select `SIPS`. A supplied rail is a closed enum: `SIPS` or `PAPSS`. PAPSS selection is allowed only when the deployment-level `PapssFacing:Enabled` switch is on. Each deployment represents one bank and uses its existing `Xades:BIC` as the local participant identity; request bodies and HTTP identity claims cannot select another participant.

PAPSS adds the same optional `rail` field to the existing request mappings. For payments, sender country and permitted sender currency come from flat local configuration; contradictory legacy JSON values fail closed. Receiver country comes from PAPSS discovery, while receiver currency is transaction-selected where needed and validated against PAPSS readiness. These values remain carried in signed `pacs.008` supplementary data. PAPSS status additionally needs `endToEnd`, `txId`, and `toBIC`; returns need `toBIC`, `originalAmount`, and `originalCurrency`.

The additional participant-facing JSON endpoints are:

- `POST /api/v1/Gateway/Readiness`
- `POST /api/v1/Gateway/Discovery`
- `POST /api/v1/Gateway/FX`

They require `rail: "PAPSS"`, use the existing Gateway authorization role, pass through `JsonAdapter`, and emit/consume the schema-backed `admi.009`/`admi.010` WP-SIPS profiles.

## Configuration

`PapssFacing` is disabled by default. When enabled, startup requires an absolute `IsoIngressUrl`, an explicit matching `AllowedHosts` entry, local country and sending currencies, remote WP-SIPS identity, environment, security profile, bounded timeout/response size, and callback routing. The local BIC, signing certificate, private key, chain, algorithm and trust/discovery settings remain in the existing `Xades` and `Core` sections.

Example:

```json
{
  "PapssFacing": {
    "Enabled": true,
    "IsoIngressUrl": "https://papss.example/sips/messages",
    "Environment": "test",
    "RemoteWpSipsIdentity": "PAPSS",
    "SecurityProfile": "WP-SIPS-XADES-SHA256RSA",
    "RequestTimeoutSeconds": 15,
    "MaximumResponseBytes": 2000000,
    "AllowedHosts": ["papss.example"],
    "SpsPolicy": { "AllowedLocalInstruments": [] },
    "ReadinessStaleSeconds": 300,
    "LocalCountry": "SO",
    "SendingCurrencies": ["SOS"],
    "CallbackMappingProfile": "participant-callback-v1",
    "CallbackUrl": "https://bank.example/papss/callback"
  }
}
```

## Security and operations

Outbound traffic uses one configured PAPSS ISO ingress, `application/xml`, a correlation header, bounded time and response size, signed envelopes, response signature/provenance verification, schema/profile validation, and financial/information correlation checks. This deployment's `Xades:BIC` supplies sender BIC; flat `PapssFacing` settings supply local country and sending currencies. PAPSS is BAH `To`.

For payments, destination BIC is transaction data. Signed Discovery must return one exact BIC match, and correlated signed Readiness must be fresh, active and online. Receiver country comes from Discovery; receiver currencies and payment schemas come from Readiness. Unsupported or ambiguous observations fail closed. `SpsPolicy` is only an optional local restriction and never duplicates PAPSS directory master data.

Reverse traffic continues to enter through the existing signed-ISO `/api/v1/Incoming` endpoint. PAPSS callbacks are verified before dispatch, require BAH `To` to equal this deployment's `Xades:BIC`, and execute under an async-local callback mapping scope. Mapping keys use `<CallbackMappingProfile>.<existing mapping name>` (for example `participant-callback-v1.CB_PaymentRequest`), preserving the existing Core duplicate suppression, delivery handling, and recall behavior.

Roll out with `Enabled=false`, configure trust and the local route/callback settings, validate connectivity in a non-production environment, then enable the PAPSS rail. Rollback is setting `PapssFacing:Enabled=false`; omitted-rail requests continue down the original SmartVista/SIPS path.

## Qualified counterpart

The signed ISO boundary was bilaterally qualified with PAPSS commit `05ae5e2dc1494ca30e703bb81394d3f439ead3be`. The bank-facing request examples, callback duties, error handling, and UAT checklist are documented in `PAPSS_BANK_INTEGRATION_GUIDE.md`.
