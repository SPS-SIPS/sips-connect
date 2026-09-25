# Bank integration guide: SIPS Connect for PAPSS

## Purpose and boundary

Banks integrate with SIPS Connect through JSON over HTTPS. SIPS Connect authenticates the bank, maps its JSON to the internal model, creates and signs the required WP-SIPS ISO 20022 message, and sends it to the single PAPSS service ingress. Banks do not call `/sips/messages` and do not create WP-SIPS signatures themselves.

The qualified bilateral baseline is SIPS Connect `e60b24a018e4543b717abc110a15c03ce8795787` with PAPSS `05ae5e2dc1494ca30e703bb81394d3f439ead3be`.

## Onboarding information

Exchange the following through the approved secure onboarding channel:

- the bank's configured `Xades:BIC` and Gateway role assignment;
- the bank BIC that SIPS Connect must place in WP-SIPS BAH `From`;
- local BIC/country/sending currencies, deployment-level PAPSS route state, and any deliberate SPS instrument policy;
- the bank callback HTTPS URL and callback mapping profile;
- API credentials or JWT issuer/client details;
- participant inbound-verification certificates and callback transport/signing requirements.

Each SIPS Connect deployment represents one bank. Its `Xades:BIC` is the protected local identity and becomes BAH `From`; flat `PapssFacing` settings supply local country, sending currencies, PAPSS routing, and callback transport. API authentication remains required, but SIPS Connect does not maintain a participant registry or per-operation authorization map.

## Authentication

Every Gateway request requires a principal with the `Gateway` role. Use one of:

```http
Authorization: Bearer <JWT_TOKEN>
Content-Type: application/json
```

or:

```http
X-API-KEY: <API_KEY>
X-API-SECRET: <API_SECRET>
Content-Type: application/json
```

Credentials are participant-specific and must not be shared between banks or environments. Examples below contain placeholders only.

## Rail selection and compatibility

The existing endpoints remain `/Verify`, `/Payment`, `/Status`, and `/Return`. Their optional `rail` value is a closed enum:

- omitted or `SIPS`: use the existing SmartVista/SIPS behavior;
- `PAPSS`: use the PAPSS signed-ISO path.

The additional `/Readiness`, `/Discovery`, and `/FX` endpoints accept only `rail: "PAPSS"`. PAPSS must be enabled globally, enabled for the authenticated participant, and allowed for the requested operation.

Base path used below: `https://<SIPS_CONNECT_HOST>/api/v1/Gateway`.

## Operations

### Verify a beneficiary

`POST /Verify`

```json
{
  "rail": "PAPSS",
  "accNo": "<BENEFICIARY_ACCOUNT>",
  "accType": "<ACCOUNT_TYPE>",
  "agent": "<DESTINATION_BIC>",
  "QRCode": "<OPTIONAL_QR_CODE>"
}
```

For PAPSS, a successful request is acknowledged with:

```json
{
  "requestMessageId": "<WP_SIPS_MESSAGE_ID>",
  "code": "<ADMISSION_CODE>",
  "durablyAdmitted": true
}
```

SIPS Connect stores the verification in its PAPSS operation store before it calls WP-SIPS. So the `requestMessageId` can always be looked up, even when the call to WP-SIPS times out; see [Look up a PAPSS operation](#look-up-a-papss-operation).
- **Resending the same message id.** If you send the same `MsgId` again with the same account, type and destination, SIPS Connect does one of the following:
  - It returns the stored admission.
  - It returns the stored rejection.
  - If the earlier outcome was ambiguous, it re-submits the identical signed request, and WP-SIPS answers `EXACT_REPLAY`.
- **Reusing a message id for a different verification.** This returns `400` with `DUPLICATE_CONFLICT`.

### Look up a PAPSS operation

`GET /api/v1/Gateway/Operations/{requestMessageId}` or `GET /api/v1/Gateway/Verify/{requestMessageId}`. Both need the same `Gateway` role as `POST /Verify`. `Verify/{id}` only matches bank-initiated verifications.

The lookup returns the stored state and result through the `OperationResult` JsonAdapter mapping:

```json
{
  "requestMessageId": "SIPS-4f1c2d3e4f5a6b7c8d9e0f1a",
  "operation": "VERIFICATION",
  "direction": "OUTBOUND",
  "status": "COMPLETED",
  "gatewayState": "ADMITTED",
  "papssOutcome": "VERIFIED_MATCH",
  "bankDeliveryState": "DELIVERED",
  "verificationId": "SIPS-4f1c2d3e4f5a6b7c8d9e0f1a",
  "counterpartyBic": "<DESTINATION_BIC>",
  "verified": true,
  "accountName": "FORTRESS GLOBAL SECURITY PRINTERS(SL)LTD",
  "accountNumber": "0012030321735",
  "accountType": "BBAN",
  "currency": "SLE",
  "reason": "MATCH",
  "additionalInfo": null,
  "admissionCode": "RECEIVED_AND_DURABLY_ADMITTED",
  "reasonCode": null,
  "replyState": null,
  "createdAt": "2026-09-25T08:00:00.000Z",
  "completedAt": "2026-09-25T08:00:02.310Z",
  "deadlineAt": null,
  "ageSeconds": 42
}
```

The `status` field summarises three independent states:

| `status` | Meaning |
| --- | --- |
| `PENDING` | Admitted or being submitted, and no result yet. |
| `COMPLETED` | A PAPSS result is stored (`verified`, `accountName`, `reason`, …). This holds even if the push to your callback failed. |
| `REJECTED` | WP-SIPS rejected the request before any external effect. `admissionCode` has the code, for example `DUPLICATE_CONFLICT`. |
| `UNKNOWN` | The outcome of the submission is ambiguous (transport error or timeout). Retry `POST /Verify` with the same `MsgId`, or wait for the result. |
| `EXPIRED` | The verification was still pending after `PapssFacing:Outbound:VerificationResultExpirySeconds`. This only happens when an operator configured that setting. |
| `FAILED` | Only for enquiries that PAPSS sent to your bank: your core bank did not answer. |

The three state fields are:
- `gatewayState`: `SUBMITTING`, `ADMITTED`, `REJECTED` or `SUBMISSION_UNKNOWN`.
- `papssOutcome`: `PENDING`, `VERIFIED_MATCH`, `VERIFIED_NO_MATCH`, `REJECTED` or `UNKNOWN`.
- `bankDeliveryState`: `NOT_REQUIRED`, `PENDING`, `DELIVERED` or `FAILED`, for the callback push.

An unknown id returns `404` with `OPERATION_NOT_FOUND`. The optional `?waitSeconds=N` parameter long-polls while the status is `PENDING`. It is capped by `PapssFacing:Lookup:MaxWaitSeconds`, which defaults to `0`, meaning long-polling is off. Use the lookup as a fallback or for reconciliation. The push callback is still the primary channel.

### Submit a payment

`POST /Payment`

```json
{
  "rail": "PAPSS",
  "agent": "<DESTINATION_BIC>",
  "lclInstrument": "<LOCAL_INSTRUMENT>",
  "ctgPurp": "<CATEGORY_PURPOSE>",
  "localId": "<UNIQUE_END_TO_END_ID>",
  "txId": "<UNIQUE_TRANSACTION_ID>",
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
```

The debtor-agent, sender country and sender currency authority comes from the authenticated local participant configuration; contradictory legacy JSON values are rejected. Destination BIC is transaction data. Receiver country, supported receiver currencies and payment schemas are resolved through signed, correlated, freshness-checked Discovery and Readiness calls. Select receiver currency when more than one is available. Reuse neither `localId` nor another transaction's references.

The response uses the same admission shape shown for `/Verify`.

### Query payment status

`POST /Status`

```json
{
  "rail": "PAPSS",
  "endToEnd": "<ORIGINAL_END_TO_END_ID>",
  "txId": "<ORIGINAL_TRANSACTION_ID>",
  "toBIC": "<DESTINATION_BIC>"
}
```

The response uses the common PAPSS admission shape. Final or subsequent status may also arrive through the configured callback channel.

### Return a payment

`POST /Return`

```json
{
  "rail": "PAPSS",
  "toBIC": "<DESTINATION_BIC>",
  "lclInstrument": "<ORIGINAL_LOCAL_INSTRUMENT>",
  "ctgPurp": "<ORIGINAL_CATEGORY_PURPOSE>",
  "originalAmount": 125.50,
  "originalCurrency": "<ISO_4217>",
  "txId": "<ORIGINAL_TRANSACTION_ID>",
  "endToEndId": "<ORIGINAL_END_TO_END_ID>",
  "reason": "<RETURN_REASON_CODE>",
  "additionalInfo": "<RETURN_REASON_TEXT>",
  "returnId": "<UNIQUE_RETURN_ID>"
}
```

Unsupported recall semantics are refused. The response uses the common PAPSS admission shape.

### Check PAPSS readiness

`POST /Readiness`

```json
{
  "rail": "PAPSS",
  "papssId": "<OPTIONAL_PAPSS_PARTICIPANT_ID>",
  "bic": "<OPTIONAL_BIC>"
}
```

The response contains either `observation` or `error`. Readiness is current PAPSS operational and capability evidence; local participant enablement and any explicitly configured SPS policy remain independent controls.

### Discover participants

`POST /Discovery`

```json
{
  "rail": "PAPSS",
  "online": true,
  "type": "<OPTIONAL_PARTICIPANT_TYPE>",
  "bic": "<OPTIONAL_BIC>",
  "papssId": "<OPTIONAL_PAPSS_PARTICIPANT_ID>"
}
```

The response contains either `participants` or `error`.

### Obtain FX information

`POST /FX`

```json
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
```

The response contains `rates`, calculated amount objects where applicable, or `error`.

### Read the bank's PAPSS position

`GET /api/v1/Gateway/PAPSS/Positions?limit=20`

The response is newest-first and contains PAPSS `rcon.001` snapshots for the authenticated bank: opening and closing balance, currency, sent/received/total amounts, transaction counts, fees, PAPSS processing time, and queue metadata where PAPSS supplied those summary fields. The raw snapshot is retained even when PAPSS sends a position variant without the optional summary. The closing balance is PAPSS's reported position as of `processedAt`; it is not an SPS ledger balance or a guarantee of funds after that timestamp. The request deliberately has no BIC or PAPSS participant-ID parameter: SIPS Connect signs with its configured bank identity, and the technical connector binds that identity first to the local SPS whitelist and then to PAPSS's current participant directory.

## Errors and retry behavior

Error bodies use `code` and `message` where PAPSS routing or validation fails.

| HTTP status | Meaning | Bank action |
| --- | --- | --- |
| `400` | Rail, participant binding, PAPSS enablement, or operation permission failed | Correct configuration/request; do not blind-retry |
| `401` / `403` | Authentication or Gateway role failed | Refresh credentials or correct authorization |
| `422` | PAPSS authority, directory capability, or transaction validation failed | Correct business data or wait for an eligible fresh directory observation; do not retry unchanged |
| `502` | Signed response was invalid or could not be authenticated | Preserve references; escalate, do not create a replacement payment |
| `503` | PAPSS/WP-SIPS service unavailable | Retry with bounded backoff using the same business references |

An HTTP success confirms the returned admission result; it does not permit reuse of message or transaction identifiers. On a timeout or ambiguous delivery, query `/Status` with the original references before deciding on another business action.

## Callback contract

The bank supplies one HTTPS callback URL and a `CallbackMappingProfile`. SIPS Connect verifies PAPSS-originated XAdES signatures, checks the local BIC and correlation, applies the deployment's `JsonAdapter` callback mappings, and delivers the resulting JSON to that URL.

The callback mapping keys are `<profile>.<callback-name>`, for example `bank-uat-v1.CB_PaymentRequest`. The profile must define every callback the participant enables. Callback authentication/signing is agreed during onboarding; secrets and private keys are deployed through the approved secret store, never in adapter JSON or this document.

### Verification (name enquiry) result callback

`POST /api/v1/Gateway/Verify` with `"rail": "PAPSS"` returns only a technical admission (`requestMessageId`, `code`, `durablyAdmitted`). The business result arrives later from PAPSS as a signed `acmt.024.001.03`; SIPS Connect validates it and delivers it to the callback URL using the mapping `<profile>.CB_VerificationResult` (for example `papss-callback-v1.CB_VerificationResult`). Baseline JSON:

```json
{
  "requestMessageId": "SIPS-4f1c2d3e4f5a6b7c8d9e0f1a",
  "originalMsgId": "SIPS-4f1c2d3e4f5a6b7c8d9e0f1a",
  "verificationId": "SIPS-4f1c2d3e4f5a6b7c8d9e0f1a",
  "verified": true,
  "accountNumber": "0012030321735",
  "accountType": "BBAN",
  "accountName": "FORTRESS GLOBAL SECURITY PRINTERS(SL)LTD",
  "currency": "SLE",
  "reason": "MATCH",
  "additionalInfo": null,
  "fromBIC": "<RESPONDING-BIC>",
  "toBIC": "<YOUR-BIC>",
  "responseMessageId": "<acmt.024 BizMsgIdr>"
}
```

Correlate on `requestMessageId` (the value returned by `/Verify`; for PAPSS verifications SIPS Connect uses one identifier for the request BizMsgIdr, MsgId and verification id). A `verified: false` result carries `reason` (for example `MISS`) and the enquired `accountNumber`/`accountType`; `accountName` and `currency` are then `null`. The request carries `X-Idempotency-Key: <verificationId>`. Process redeliveries idempotently.

The callback is pushed asynchronously, from a durable outbox:
- SIPS Connect first stores the signed acmt.024. It de-duplicates on the acmt.024 AppHdr `BizMsgIdr`, so a PAPSS redelivery is stored and pushed only once. Only then does it acknowledge PAPSS.
- A worker then delivers the result to your callback.
- A 2xx from your callback marks the push `DELIVERED`.
- Any other status, or a transport error, is retried with exponential backoff. The defaults are 5 s initial, 300 s maximum and 10 attempts (`PapssFacing:Delivery:*`). After the last attempt the push is marked `FAILED`, and the result stays available from the lookup API.
- A slow or failing callback no longer makes PAPSS redeliver the result.
- Delivery is at-least-once, so you may see the same `verificationId` more than once.

If a result arrives for a `requestMessageId` that SIPS Connect does not hold, it is still stored and pushed. An example is a verification submitted before the operation store was deployed. A second, different result for an operation that is already completed is stored for audit and is not pushed.

### Verification enquiries from other PAPSS countries

When a participant in another country verifies one of your accounts:
1. PAPSS delivers a signed acmt.023 to SIPS Connect.
2. SIPS Connect stores the enquiry, keyed by the PAPSS source message id.
3. SIPS Connect calls your core bank through the existing `CB_VerificationRequest` / `CB_VerificationResponse` mappings, within `Core:CoreBankTimeoutSeconds`.
4. SIPS Connect queues a signed acmt.024.001.03 answer for WP-SIPS and submits it from an outbox, re-sending the identical bytes if a retry is needed.

The behaviour to expect:
- **Redelivered enquiries.** PAPSS may redeliver an enquiry until SIPS Connect acknowledges it. A redelivery never calls your core bank a second time and never produces a different answer.
- **Reason.** The answer carries exactly the `reason` your core bank returned. If you return no reason, the acmt.024 has no reason, because SIPS Connect does not add a default.
- **Core bank failure.** If your core bank does not answer (timeout, 5xx or 4xx), SIPS Connect sends **no** answer. The enquiry is recorded as `FAILED` / `UNKNOWN`. Whether a participant may send a negative answer in that case, and with which code, is not yet established by PAPSS; see [PAPSS_OPERATION_STORE_CONFIG.md](PAPSS_OPERATION_STORE_CONFIG.md#unresolved-papss-behaviour).
- **Lookup.** `GET /api/v1/Gateway/Operations/{PAPSS source message id}` shows the enquiry with `direction: "INBOUND"`, the answer you gave and the gateway `replyState`.

Callback receivers must:

- use HTTPS and validate the configured SIPS Connect identity;
- authenticate each request according to the agreed profile;
- process duplicate deliveries idempotently using transaction/original references;
- return the agreed acknowledgement within the callback SLA;
- preserve correlation identifiers in logs and support reconciliation.

## JsonAdapter customization

`jsonAdapter.json`, `jsonAdapter.node-a.json`, and `jsonAdapter.node-b.json` contain the baseline PAPSS request/response mappings. A deployment may change `UserField` paths to match a bank's JSON contract, but must retain the required `InternalField`, type, null/empty behavior, and endpoint mapping names.

Required mapping names are:

```text
VerificationRequest
PaymentRequest
StatusRequest
ReturnRequest
PapssAdmissionResponse
ReadinessRequest
ReadinessResponse
ParticipantDiscoveryRequest
ParticipantDiscoveryResponse
FxRequest
FxResponse
```

The current `FxResponse` fields are `Rates`, `SenderAmount`, `ExchangeAmount`, `ReceiverAmount`, `NationalFeeAmount`, `FeeAmount`, and `Error`. `InvoiceAmount` is obsolete. FX rates and amounts use decimal-safe values and must never pass through binary `double` arithmetic.

## Payment template and authority boundaries

```json
{
  "rail": "PAPSS",
  "agent": "<DESTINATION-BIC>",
  "lclInstrument": "<PAPSS-PAYMENT-SCHEMA>",
  "ctgPurp": "<CATEGORY-PURPOSE>",
  "localId": "<END-TO-END-ID>",
  "txId": "<TRANSACTION-ID>",
  "currency": "<SENDER-CURRENCY>",
  "amount": 100.00,
  "papss": { "receiverCurrency": "<RECEIVER-CURRENCY>" }
}
```

SIPS Connect derives the sender BIC and country from its participant deployment, validates sender currency against that deployment, resolves the destination by an exact unique BIC, and validates receiver country, currency, and local instrument against fresh signed PAPSS discovery/readiness observations. A bank must not send a PAPSS ParticipantId, technical channel, PAPSS endpoint, or certificate identity as routing data.

USD-to-local-currency PAPSS payment submission currently fails closed with `PAPSS_FX_FEE_NOT_READY` until the authoritative transaction FX/fee handoff is enabled. The FX endpoint remains indicative reference data only.

## Callback decision loop

HTTP success from the PAPSS callback endpoint acknowledges delivery only. After the bank returns `ACCP` or `RJCT` (with a reason for rejection), SIPS Connect creates one correlated `pacs.002.001.12`, signs it explicitly with `WpSipsPapss`, persists it, and publishes it separately to `/sips/messages`. Retries reuse the exact stored signed decision. `IpsVendorLegacy` remains exclusive to SmartVista.

## Certificate registration in Guevara

Guevara must have one active, unambiguous record for the participant signing certificate, resolved by exact issuer DN and serial number. It records the owner, represented participant BIC, environment, WP-SIPS security profile, required EKU, SHA-256 fingerprint, SPS authority, and non-revoked/non-suspended status. The private key stays inside the participant deployment. PAPSS Adapter mTLS and PAPSS XML-signing certificates are not bank configuration values.

Callback mappings remain participant-profile-prefixed and are separate from these outbound Gateway mappings.

## UAT acceptance checklist

1. Confirm `Xades:BIC` is the expected local bank BIC and the flat `PapssFacing` local/callback settings describe this deployment.
2. Confirm the deployment-level PAPSS rail remains disabled during initial deployment.
3. Verify the four legacy endpoints without `rail` still use SmartVista/SIPS.
4. Load and validate the participant JsonAdapter and callback mappings.
5. Bind certificates and secrets through the environment's secret store.
6. Enable the deployment-level PAPSS rail for UAT.
7. Exercise Verify, Payment, Status, Return, Readiness, Discovery, and FX.
8. Confirm signed-ISO correlation and asynchronous callback delivery where applicable.
9. Test duplicate callback handling, ambiguous delivery reconciliation, denied operations, stale/ambiguous/ineligible directory observations, unsupported currencies/instruments, and credential failure.
10. Record request references, expected results, timestamps, and evidence without recording secrets or sensitive account data.
