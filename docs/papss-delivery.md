Extend the SIPS Connect frontend and automated testing tools to support PAPSS using the existing implementation and the PAPSS documentation already shared.

Authoritative SIPS documentation:
- README.md — PAPSS Integration section
- docs/PAPSS_BANK_INTEGRATION_GUIDE.md
- docs/PAPSS_PARTICIPANT_ADAPTER.md
- docs/XADES_PROFILE_SEPARATION_EVIDENCE.md

Current SIPS source:
7e56160263da6dae7dc16f4192c2550bfc930292

Shared packages:
- Sps.Sips.XmlSecurity.Xades 1.0.3
- Sps.Sips.Iso20022 1.0.1

BOUNDARY

The frontend communicates only with SIPS Connect using JSON over HTTPS.

It must not:
- call PAPSS `/sips/messages` directly;
- construct ISO 20022 messages;
- generate WP-SIPS XAdES signatures;
- allow a user to override the configured sender BIC.

SIPS Connect owns JSON mapping, ISO generation, signing, correlation, and communication with PAPSS.

BASE URL

https://<SIPS_CONNECT_HOST>/api/v1/Gateway

Authentication remains unchanged:
- Bearer token with the Gateway role; or
- configured API key and API secret.

RAIL SELECTION

The existing endpoints remain unchanged:
- POST /Verify
- POST /Payment
- POST /Status
- POST /Return

They now accept an optional `rail` field:

- omitted, empty, or `SIPS` → existing domestic SmartVista/IPS path;
- `PAPSS` → PAPSS path.

For backward compatibility, prefer omitting `rail` for domestic requests rather than sending an empty string.

The new PAPSS-only endpoints are:
- POST /Readiness
- POST /Discovery
- POST /FX

These require:

{
  "rail": "PAPSS"
}

Never send another rail value. Unknown values are rejected.

FRONTEND REQUIREMENTS

1. Add a rail selector to Verify, Payment, Status, and Return:
   - Domestic SIPS
   - PAPSS

2. Default the selector to Domestic SIPS.

3. When Domestic SIPS is selected:
   - preserve the existing request shape and behavior;
   - omit `rail` where possible;
   - do not display or require PAPSS corridor fields;
   - do not change existing SmartVista workflows.

4. When PAPSS is selected:
   - send `"rail": "PAPSS"`;
   - show PAPSS-specific fields;
   - use participant and FX discovery where appropriate;
   - retain all original transaction references for status, return, and reconciliation.

5. Add PAPSS screens/actions for:
   - readiness;
   - participant discovery;
   - FX enquiry;
   - PAPSS payment submission;
   - PAPSS status enquiry;
   - PAPSS return;
   - callback/event history if the frontend already displays incoming events.

6. PAPSS should be visibly unavailable when the backend returns `PAPSS_DISABLED`. Do not silently fall back to Domestic SIPS after a user explicitly chooses PAPSS.

7. Do not expose private keys, certificate passwords, API secrets, or raw signing material in the frontend.

REQUESTS

Verify:

POST /Verify

{
  "rail": "PAPSS",
  "accNo": "<BENEFICIARY_ACCOUNT>",
  "accType": "<IBAN|MSIS|EWLT|ACCT>",
  "agent": "<DESTINATION_BIC>",
  "QRCode": "<OPTIONAL_QR_CODE>"
}

Payment:

POST /Payment

{
  "rail": "PAPSS",
  "agent": "<DESTINATION_BIC>",
  "lclInstrument": "<LOCAL_INSTRUMENT>",
  "ctgPurp": "<CATEGORY_PURPOSE>",
  "localId": "<UNIQUE_END_TO_END_ID>",
  "amount": 125.50,
  "currency": "<SETTLEMENT_CURRENCY>",
  "drName": "<DEBTOR_NAME>",
  "drAccount": "<DEBTOR_ACCOUNT>",
  "drAccountType": "<ACCOUNT_TYPE>",
  "crName": "<CREDITOR_NAME>",
  "crAccount": "<CREDITOR_ACCOUNT>",
  "crAccountType": "<ACCOUNT_TYPE>",
  "crAgentBIC": "<DESTINATION_BIC>",
  "narration": "<REMITTANCE_TEXT>",
  "papss": {
    "senderCountry": "<ISO_3166_ALPHA_2>",
    "receiverCountry": "<ISO_3166_ALPHA_2>",
    "senderCurrency": "<ISO_4217>",
    "receiverCurrency": "<ISO_4217>"
  }
}

The authenticated local participant configuration supplies sender BIC, sender country, and permitted sending currencies. Destination BIC and receiver currency are transaction selections. SIPS Connect derives receiver country and validates receiver currency and local instrument against fresh, signed PAPSS Discovery/Readiness observations; no static per-destination corridor is configured. Any SPS instrument restriction is separately identified as local SPS policy.

Status:

POST /Status

{
  "rail": "PAPSS",
  "endToEnd": "<ORIGINAL_END_TO_END_ID>",
  "txId": "<ORIGINAL_TRANSACTION_ID>",
  "toBIC": "<DESTINATION_BIC>"
}

Do not create new identifiers for a status request. Use the references returned or recorded for the original payment.

Return:

POST /Return

{
  "rail": "PAPSS",
  "toBIC": "<DESTINATION_BIC>",
  "originalAmount": 125.50,
  "originalCurrency": "<ISO_4217>",
  "txId": "<ORIGINAL_TRANSACTION_ID>",
  "endToEndId": "<ORIGINAL_END_TO_END_ID>",
  "reason": "<RETURN_REASON_CODE>",
  "additionalInfo": "<RETURN_REASON_TEXT>",
  "returnId": "<UNIQUE_RETURN_ID>"
}

Recall is not supported. Do not represent Return as Recall in the UI.

Readiness:

POST /Readiness

{
  "rail": "PAPSS",
  "papssId": "<OPTIONAL_PAPSS_PARTICIPANT_ID>",
  "bic": "<OPTIONAL_BIC>"
}

Readiness is PAPSS operational and capability information. The deployment-level PAPSS rail switch and any explicit SPS policy remain separate controls.

Discovery:

POST /Discovery

{
  "rail": "PAPSS",
  "online": true,
  "type": "<OPTIONAL_PARTICIPANT_TYPE>",
  "bic": "<OPTIONAL_BIC>",
  "papssId": "<OPTIONAL_PAPSS_PARTICIPANT_ID>"
}

FX:

POST /FX

{
  "rail": "PAPSS",
  "senderCountry": "<ISO_3166_ALPHA_2>",
  "receiverCountry": "<ISO_3166_ALPHA_2>",
  "senderCurrency": "<ISO_4217>",
  "receiverCurrency": "<ISO_4217>",
  "receiverBank": "<DESTINATION_BIC>",
  "localInstrument": "<LOCAL_INSTRUMENT>",
  "amount": 125.50,
  "isInvoice": false,
  "invoiceCurrency": "<OPTIONAL_ISO_4217>"
}

RESPONSES

Verify, Payment, Status, and Return use the PAPSS admission response:

{
  "requestMessageId": "<WP_SIPS_MESSAGE_ID>",
  "code": "<ADMISSION_CODE>",
  "durablyAdmitted": true
}

Do not display `durablyAdmitted=true` as final settlement. It means PAPSS durably accepted the request. Final status may require `/Status` or an asynchronous callback.

Readiness responses contain an observation or error.
Discovery responses contain participants or error.
FX responses contain rates/calculated amounts or error.

Render unknown additional response properties safely so compatible server-side extensions do not break the frontend.

ERROR HANDLING

Expect:

- 400: invalid rail, PAPSS disabled, participant not enabled, or operation denied;
- 401/403: authentication or Gateway-role failure;
- 422: PAPSS business/corridor validation failure;
- 502: invalid or unauthenticated signed PAPSS response;
- 503: PAPSS service unavailable.

Error payloads normally contain:

{
  "code": "<ERROR_CODE>",
  "message": "<SAFE_MESSAGE>"
}

Rules:
- never silently switch rails;
- do not blindly retry 400, 401, 403, or 422;
- on 502, timeout, or ambiguous payment delivery, preserve the original references and query `/Status`;
- on 503, use bounded retry/backoff;
- prevent double submission while a financial request is pending;
- never generate a new `localId` merely because the first response timed out.

JSON ADAPTER REQUIREMENTS

Extend frontend test fixtures and test utilities for these mapping names:

- VerificationRequest
- PaymentRequest
- StatusRequest
- ReturnRequest
- PapssAdmissionResponse
- ReadinessRequest
- ReadinessResponse
- ParticipantDiscoveryRequest
- ParticipantDiscoveryResponse
- FxRequest
- FxResponse

The adapter’s `InternalField` names are the stable SIPS contract. `UserField` names are participant-facing and may be customized.

For inbound core-bank verification callbacks, the current Zirat and Agro mock contract requires:

{
  "InternalField": "Type",
  "UserField": "accountType"
}

Do not rename `InternalField: "Type"`.

AUTOMATED TESTING

Add unit, integration, and UI tests proving:

1. Existing Verify/Payment/Status/Return requests with omitted `rail` remain on Domestic SIPS.
2. Explicit `rail: "SIPS"` remains on Domestic SIPS.
3. Empty or missing rail preserves legacy behavior.
4. `rail: "PAPSS"` routes through PAPSS.
5. Unknown rail values fail and never fall back.
6. Readiness/Discovery/FX reject missing rail.
7. Readiness/Discovery/FX reject `rail: "SIPS"`.
8. PAPSS-disabled responses are displayed clearly.
9. Participant-disabled and operation-denied responses are displayed clearly.
10. Valid and invalid corridors are covered.
11. Required payment fields are validated before submission.
12. ISO country codes use two letters and currencies use ISO 4217 codes.
13. Amounts use decimal-safe handling; do not use floating-point arithmetic for financial calculations.
14. Payment identifiers remain stable across timeout/status reconciliation.
15. Duplicate clicks do not create duplicate payments.
16. Admission is not displayed as final settlement.
17. Status and return reuse original transaction references.
18. Discovery selections populate the destination BIC without changing the configured sender BIC.
19. FX output is associated with the exact requested corridor, amount, and destination.
20. Callback/event rendering is idempotent for duplicate delivery.
21. Callback correlation is visible without exposing sensitive payload data.
22. Domestic SmartVista regression remains unchanged.
23. Zirat and Agro verification callback fixtures send `accountType`, including `ACCT`.
24. Error and timeout logs redact credentials, certificates, private keys, account secrets, and tokens.

UAT FLOW

Execute in this order:

1. PAPSS disabled: run the complete existing Domestic SIPS regression.
2. Confirm Readiness/Discovery/FX are unavailable while PAPSS is disabled.
3. Enable the deployment-level PAPSS rail for the UAT bank.
4. Run Readiness.
5. Run Discovery.
6. Run FX.
7. Run Verify.
8. Run Payment.
9. Run Status using the original references.
10. Run Return using the original references.
11. Confirm applicable asynchronous callbacks.
12. Exercise invalid corridor, denied operation, timeout, duplicate submission, and ambiguous-delivery cases.
13. Record correlation IDs, timestamps, request identifiers, and sanitized evidence.

DELIVERABLES

- updated frontend screens and API client;
- typed request/response models for all seven operations;
- updated mock data and automated tests;
- environment-safe configuration;
- callback/event display where applicable;
- a UAT execution report;
- proof that the existing Domestic SIPS flows remain unchanged.

Do not modify SIPS Connect’s signed-ISO or XAdES implementation from the frontend project.
