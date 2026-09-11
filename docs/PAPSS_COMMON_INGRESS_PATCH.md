# Required PAPSS amendment: common signed ISO ingress

Target repository: `/Users/maven/source/art/papss`
Assessed baseline: `41f8f44fb8b65bb61fcc2ef156777ff56eba1cac`

This specification is intentionally implementation-ready but was not applied because PAPSS is outside the authorized writable repository.

## Required patch

1. Add `POST /sips/messages` to `src/Sps.Papss.Service/Program.cs`. Accept only `application/xml` (optionally `text/xml`), enforce a configured body-size limit and cancellation timeout, and return a signed XML envelope with the same media type. Remove no existing route during migration.
2. Add `SipsMessageIngress` beside `SipsFinancialIngress`. It must call the existing WP-SIPS strict parser/validator, verify XAdES signature and trusted signer provenance before dispatch, and never dispatch unverified payloads.
3. Dispatch on the validated `MsgDefIdr` plus `BizSvc`, never on caller headers or XML element-name heuristics:
   - payee verification request -> existing verification application service;
   - `pacs.008` payment -> payment application service;
   - `pacs.028` status -> status application service;
   - `pacs.004` return -> return application service;
   - `admi.009` + `SPS.PAPSS.READINESS.001` -> `WpSipsAdapter.GetParticipantReadinessAsync`;
   - `admi.009` + `SPS.PAPSS.PARTICIPANT.001` -> `WpSipsAdapter.DiscoverParticipantsAsync`;
   - `admi.009` + `SPS.PAPSS.FX.001` -> `WpSipsAdapter.GetFxRatesAsync`.
4. Parse `urn:sps:papss:corridor:001` supplementary data on `pacs.008`, require all four corridor fields, validate ISO country/currency shapes, and reject contradictions between sender currency and the base settlement currency. Pass the values unchanged into PAPSS admission/quote logic.
5. Build schema-valid correlated responses, populate related business-message identity for information responses, sign every success and every protocol rejection, and use `admi.002` with stable reason codes for unsupported profiles, schema failure, bad correlation, authorization failure, or invalid signature.
6. Configure trusted SIPS identities/certificates, local signing material, accepted profile, maximum request bytes, timeout, and replay window. Fail startup when enabled configuration is incomplete. Never log XML, credentials, private material, or personal/payment values.

## Acceptance tests in PAPSS

- Each of the seven operations enters through `/sips/messages`; no adapter operation needs an operation-specific PAPSS URL.
- A valid signed request produces a valid, signed, correlated response that SIPS Connect can verify.
- Invalid signature, untrusted signer, malformed XML, DTD/entity input, wrong schema, unsupported `BizSvc`, replay, and oversized body are rejected before application dispatch.
- Readiness, discovery, and FX exercise the existing `WpSipsAdapter` and preserve typed success/error payloads.
- A payment round-trip preserves sender country, receiver country, sender currency, and receiver currency; missing or contradictory values are rejected.
- Concurrent duplicate payment/correlation identifiers remain idempotent.
- Existing `/sips/payments` behavior is covered during the migration window and explicitly deprecated.

## Cross-repository conformance gate

Start PAPSS with test trust/signing material and point SIPS Connect `PapssFacing:IsoIngressUrl` to `/sips/messages`. Run all seven operations, negative signature/schema/profile/correlation cases, timeout and response-size cases, then assert that omitted-rail legacy requests still reach SmartVista. Production enablement is blocked until this gate passes.
