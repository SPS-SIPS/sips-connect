# SIPS Connect PAPSS participant adapter

## Contract and compatibility

The existing authenticated JSON endpoints remain unchanged:

- `POST /api/v1/Gateway/Verify`
- `POST /api/v1/Gateway/Payment`
- `POST /api/v1/Gateway/Status`
- `POST /api/v1/Gateway/Return`

An omitted `rail` continues to select `SIPS`. A supplied rail is a closed enum: `SIPS` or `PAPSS`. PAPSS selection is allowed only when the global feature is enabled and the authenticated API-key/JWT participant is enabled for that operation. The participant identity is derived from the authenticated principal; request bodies cannot select another participant.

PAPSS adds the same optional `rail` field to the existing request mappings. A PAPSS payment also supplies `papss.senderCountry`, `papss.receiverCountry`, `papss.senderCurrency`, and `papss.receiverCurrency`. These values are carried in signed `pacs.008` supplementary data and are recovered when the ISO message is parsed. PAPSS status additionally needs `endToEnd`, `txId`, and `toBIC`; returns need `toBIC`, `originalAmount`, and `originalCurrency`.

The additional participant-facing JSON endpoints are:

- `POST /api/v1/Gateway/Readiness`
- `POST /api/v1/Gateway/Discovery`
- `POST /api/v1/Gateway/FX`

They require `rail: "PAPSS"`, use the existing Gateway authorization role, pass through `JsonAdapter`, and emit/consume the schema-backed `admi.009`/`admi.010` WP-SIPS profiles.

## Configuration

`PapssFacing` is disabled by default. When enabled, startup requires an absolute `IsoIngressUrl`, an explicit matching `AllowedHosts` entry, local and remote WP-SIPS identities, environment, security profile, bounded timeout/response size, and per-participant capability entries. Signing certificate, private key, chain, algorithm and trust/discovery settings remain in the existing `Xades` and `Core` sections so there is only one signing and trust policy.

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
    "AllowedLocalInstruments": ["INST"],
    "AllowedCorridors": [{
      "SenderCountry": "SO", "ReceiverCountry": "KE",
      "SenderCurrency": "SOS", "ReceiverCurrency": "KES",
      "DestinationBic": "BANKKE00XXX", "LocalInstruments": ["INST"]
    }],
    "ReadinessStaleSeconds": 300,
    "Participants": {
      "participant-api-key-name": {
        "Enabled": true,
        "Bic": "BANKSOSIXXX",
        "AllowedOperations": ["Verification", "Payment", "Status", "Return", "Readiness", "Discovery", "Fx"],
        "CallbackMappingProfile": "participant-callback-v1",
        "CallbackUrl": "https://bank.example/papss/callback"
      }
    }
  }
}
```

## Security and operations

Outbound traffic uses one configured PAPSS ISO ingress, `application/xml`, a correlation header, bounded time and response size, signed envelopes, response signature/provenance verification, schema/profile validation, and financial/information correlation checks. The authenticated principal selects exactly one configured participant BIC, which becomes BAH `From`; PAPSS is BAH `To`. Response signer identity, BAH reversal, message type, service, original message identifier, and applicable transaction references are checked before parsing.

Reverse traffic continues to enter through the existing signed-ISO `/api/v1/Incoming` endpoint. PAPSS callbacks are verified before dispatch, resolved by BAH `To` to one configured participant, and execute under an async-local callback mapping scope. Mapping keys use `<CallbackMappingProfile>.<existing mapping name>` (for example `participant-callback-v1.CB_PaymentRequest`), so the accepted duplicate suppression, ambiguous-delivery handling, and recall refusal remain in the existing Core handlers while participant-specific JSON shapes are selected safely per request.

Roll out with `Enabled=false`, configure trust and participant capabilities, validate connectivity in a non-production environment, then enable one participant and operation at a time. Rollback is setting `PapssFacing:Enabled=false`; omitted-rail requests continue down the original SmartVista/SIPS path.

## Known upstream dependency

At PAPSS commit `41f8f44fb8b65bb61fcc2ef156777ff56eba1cac`, the service exposes only `POST /sips/payments` as unsigned JSON. That is incompatible with the signed ISO-only boundary required by this adapter. Do not enable PAPSS routing until the patch in `PAPSS_COMMON_INGRESS_PATCH.md` is implemented and its conformance tests pass.
