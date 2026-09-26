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

All lookups need the `Gateway` role and return the same `OperationResult` payload:

| Request | Finds |
| --- | --- |
| `GET /api/v1/Gateway/Operations/{requestMessageId}` | Any stored PAPSS operation by the `requestMessageId` returned to you (verification, payment, return, status enquiry), or an inbound one by its PAPSS source message id. |
| `GET /api/v1/Gateway/Verify/{requestMessageId}` | Bank-initiated verifications only. |
| `GET /api/v1/Gateway/Payment/{txId}` | The payment with this TxId. Your own (outbound) payment wins over a received one with the same TxId. |
| `GET /api/v1/Gateway/Return/{returnId}` | The return with this `returnId` (outbound first, then received). |
| `GET /api/v1/Gateway/Recall/{recallId}` | Your recall (camt.056) with this `recallId`, with its answers and the recalled payment's outcome. `Operations/{recallId}` returns the same. |
| `GET /api/v1/Gateway/Operations?endToEndId=...` | The payment with this EndToEndId (outbound first, newest first). |
| `GET /api/v1/Gateway/Operations/Unresolved?limit=50` | Operator view (`Gateway` or `Recon` role): received pacs.002/pacs.004/camt.029 that matched no operation, conflict with a final status, or carry an unknown status (`eventType` tells payment and recall answers apart). |

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

Payments and returns add these fields (they are absent for verifications):

```json
{
  "requestMessageId": "ZKBASOS06268081234567890123",
  "operation": "PAYMENT",
  "direction": "OUTBOUND",
  "status": "COMPLETED",
  "gatewayState": "ADMITTED",
  "papssOutcome": "SETTLED",
  "bankDeliveryState": "DELIVERED",
  "admissionCode": "RECEIVED_AND_DURABLY_ADMITTED",
  "txId": "<TX_ID>",
  "endToEndId": "<END_TO_END_ID>",
  "msgId": "ZKBASOS06268081234567890456",
  "amount": 125.5,
  "currency": "USD",
  "localInstrument": "USDP",
  "paymentStatus": "ACSC",
  "paymentOutcome": "SETTLED",
  "statusReasonCode": null,
  "statusAt": "2026-09-25T08:00:04.120Z",
  "statusConflict": false,
  "returns": ["OUTBOUND RTN-1 SETTLED"],
  "statusHistory": [
    { "receivedAt": "2026-09-25T08:00:01.020Z", "messageType": "pacs.002.001.12", "sourceMessageId": "<PAPSS id>", "status": "ACSP", "reasonCode": null, "correlation": "MSG_ID", "disposition": "APPLIED", "pushState": "DELIVERED", "note": "amount not reported by PAPSS (LOCAL_RECONSTRUCTION: the gateway supplied the original payment amount); not compared",
      "amount": 10.0, "currency": "USD", "amountSource": "LOCAL_RECONSTRUCTION", "categoryPurposeSource": null, "rawEvidenceReference": "sha256:<hex>",
      "fieldProvenance": { "TxInfAndSts/OrgnlTxRef/IntrBkSttlmAmt": "LOCAL_RECONSTRUCTION", "TxInfAndSts/OrgnlTxId": "IDENTIFIER_TRANSLATION", "TxInfAndSts/TxSts": "NETWORK_REPORTED" } },
    { "receivedAt": "2026-09-25T08:00:04.120Z", "messageType": "pacs.002.001.12", "sourceMessageId": "<PAPSS id>", "status": "ACSC", "reasonCode": null, "correlation": "MSG_ID", "disposition": "APPLIED", "pushState": "DELIVERED", "note": null,
      "amount": 10.0, "currency": "USD", "amountSource": "NETWORK_REPORTED", "categoryPurposeSource": null, "rawEvidenceReference": "sha256:<hex>", "fieldProvenance": { "TxInfAndSts/OrgnlTxRef/IntrBkSttlmAmt": "NETWORK_REPORTED" } }
  ]
}
```

- `paymentStatus` is the raw ISO status in effect (`ACCP`, `ACSP`, `ACSC`, `PDNG`, `RJCT`); `paymentOutcome` (same as `papssOutcome`) is `PENDING`, `ACCEPTED` (ACCP/ACSP), `SETTLED` (ACSC), `REJECTED` (RJCT), `RETURNED` (a return settled against the payment) or `UNKNOWN`. `SETTLED`, `REJECTED` and `RETURNED` are final and never go back.
- `statusConflict: true` means PAPSS reported a different final status after a final one. The first final status is kept; contact SPS operations before acting on either.
- A return shows `returnId`, `originalTxId`, `originalEndToEndId` and `originalRequestMessageId` (the payment it returns). A payment lists its `returns`.
- A payment you recalled lists its `recalls` (oldest first): `recallId`, `status`, `papssOutcome` (`RECALL_*`), `gatewayState`, `reason` (yours), `answerReasonCode`, `returnId` (when the funds came back), `createdAt`, `completedAt`, `deadlineAt`, `responseOverdue`, `statusConflict`. A recall never changes the payment's `paymentStatus`/`paymentOutcome`; only a received pacs.004 makes it `RETURNED`. See [Recall a payment](#recall-a-payment).
- A received (INBOUND) payment shows your decision (`paymentStatus` `ACCP` or `RJCT`) and `decisionState` (`NOT_QUEUED`, `PENDING`, `PUBLISHED`, `FAILED`) for the signed decision SIPS Connect sends to PAPSS; a later PAPSS final status updates `paymentStatus`/`paymentOutcome`.
- `statusHistory` lists every status message received for the operation, including the ones that did not change it (`NOT_ADVANCING`, `DUPLICATE_FINAL`, `CONFLICT`).
- Each `statusHistory` entry links three evidence layers: the raw PAPSS message (`sourceMessageId`, `rawEvidenceReference` = SHA-256 of the signed PAPSS message the gateway keeps), the normalized event (`status`, `reasonCode`, `amount`, `currency`, ...) and the gateway's per-field provenance (`amountSource`, `categoryPurposeSource`, `fieldProvenance`). Sources: `NETWORK_REPORTED` (PAPSS sent it), `LOCAL_RECONSTRUCTION` (the gateway filled it from the original payment it stored; PAPSS did not report it), `IDENTIFIER_TRANSLATION` (your own MsgId/TxId/EndToEndId restored), `DEFAULT_FILLER` (a message-builder placeholder such as `NA`) and `UNSPECIFIED_LEGACY` (the callback carried no provenance). An amount marked `LOCAL_RECONSTRUCTION` is not a PAPSS confirmation of the amount: SIPS Connect does not compare it with your payment and never stores it as the payment amount.

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

The response uses the same admission shape shown for `/Verify`; its `requestMessageId` is the lookup key of the payment (`GET /api/v1/Gateway/Operations/{requestMessageId}` or `GET /api/v1/Gateway/Payment/{txId}`).

`txId` is the idempotency key. SIPS Connect stores the payment and its signed pacs.008 before contacting WP-SIPS:
- Sending the same `txId` with the same content again never creates a second payment. You get the stored admission back, or, if the earlier attempt ended ambiguously (`503`/timeout), SIPS Connect re-submits the identical signed message and WP-SIPS answers `EXACT_REPLAY`.
- Sending the same `txId` with different content returns `400` `DUPLICATE_CONFLICT`; nothing is sent.
- A request refused by validation (`422`) or directory checks is not stored; correct it and retry with the same `txId`.

Status results (ACCP/ACSP/ACSC/PDNG/RJCT) arrive later on the payment status callback (see [Callback contract](#payment-and-return-status-callback)); several may arrive for one payment.

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

SIPS Connect answers from its operation store, which is authoritative:
- **Final payment** (`SETTLED`, `REJECTED`, `RETURNED`): the stored state is returned and no pacs.028 is sent.
- **Non-final payment of yours** (`PENDING`, `ACCEPTED`, `UNKNOWN`): SIPS Connect sends one pacs.028 status enquiry to PAPSS, records it (operation `STATUS_ENQUIRY`, linked to the payment) and returns the stored state together with the enquiry admission. The answer arrives as an ordinary status callback. An operator may set `PapssFacing:Status:EnquiryMinimumAgeSeconds` to answer young payments from the store only; by default it is unset.
- **Payment you received** (INBOUND): the stored state; SIPS Connect does not enquire.
- **TxId not in the store**: unchanged behaviour. A pacs.028 is sent and the admission shape (`requestMessageId`, `code`, `durablyAdmitted`) is returned.

A stored payment is returned with the `PaymentResponse` mapping plus stored fields:

```json
{
  "localId": "<END_TO_END_ID>",
  "status": "ACSP",
  "reason": null,
  "additionalInfo": null,
  "acceptanceDate": null,
  "acceptedAtUtc": null,
  "transactionId": "<TX_ID>",
  "requestMessageId": "<requestMessageId of the payment>",
  "paymentOutcome": "ACCEPTED",
  "operation": { "...": "the OperationResult of the payment, as on the lookup API" },
  "statusEnquiry": { "requestMessageId": "<requestMessageId of the pacs.028>", "code": "RECEIVED_AND_DURABLY_ADMITTED", "durablyAdmitted": true }
}
```

`status` is `null` until PAPSS has reported a status. `statusEnquiry` is `null` when no pacs.028 was sent; when one was attempted but not admitted, `code` carries the reason (for example `WP_SIPS_UNAVAILABLE`) and the stored state is still returned. An `endToEnd` that does not match the stored payment returns `422`.

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

`/Return` sends a pacs.004; to ask the beneficiary bank to give back a payment **you** sent, use [`/Recall`](#recall-a-payment). The response uses the common PAPSS admission shape.

`returnId` is the idempotency key, with the same rules as `txId` for payments (same content = stored admission or identical re-submission; different content = `400` `DUPLICATE_CONFLICT`). The return is linked to the payment it returns when that payment is in the store; a payment you received is the usual case. Its result arrives on the payment status callback with `X-Return-Id`; once it settles, the returned payment shows `paymentOutcome: "RETURNED"`. PAPSS confirmed on 2026-09-26 that the returner's authoritative return status is `ACCP`; SIPS Connect settles a return on `PapssFacing:Returns:SettledStatuses` (default `ACCP,ACSC`).

### Recall a payment

`POST /api/v1/Gateway/Recall` (role `Gateway`, JsonAdapter mapping `RecallRequest`) asks PAPSS to recall one of **your settled PAPSS payments** (camt.056):

```json
{
  "rail": "PAPSS",
  "txId": "<YOUR_ORIGINAL_TX_ID>",
  "endToEndId": "<YOUR_ORIGINAL_END_TO_END_ID>",
  "reason": "DUPL",
  "recallId": "SIPS-0123456789abcdef01234567"
}
```

- Name the payment by `txId` or `endToEndId` (both: they must belong to the same payment). `rail` is optional; anything but `PAPSS` is refused.
- `reason` is required: an ISO cancellation reason code, 1-4 letters/digits. It is passed to PAPSS unchanged; PAPSS said "use what is applicable" (`DUPL` is accepted in every PAPSS source).
- `recallId` is optional. When you send it, it must be `SIPS-` followed by 24 lowercase hexadecimal characters, and it is your idempotency key (same rules as `txId` for payments: same request = stored admission or identical re-submission after an ambiguous attempt; different reason or payment = `400 DUPLICATE_CONFLICT`). Without it SIPS Connect generates one.
- The response is the common admission shape; `requestMessageId` is the `recallId`. Only a technical admission: PAPSS answers later.

Refusals (nothing is sent to PAPSS):

| HTTP | `code` | Meaning |
| --- | --- | --- |
| `404` | `ORIGINAL_PAYMENT_NOT_FOUND` | No PAPSS payment sent by your bank is stored for this `txId`/`endToEndId`. |
| `422` | `RECALL_NOT_ALLOWED` | The payment was received by your bank. Only the payer's bank (original debtor agent) can recall. |
| `409` | `ORIGINAL_NOT_SETTLED` | The payment is not `SETTLED` (PAPSS refuses other statuses, error 1017). Operators can relax this with `PapssFacing:Recall:RequireSettledOriginal=false`; a `REJECTED` or `RETURNED` payment is never recallable. |
| `409` | `RECALL_WINDOW_EXPIRED` | The payment settled more than 30 days ago (PAPSS-confirmed; `PapssFacing:Recall:MaxAgeDays`). |
| `409` | `RECALL_ALREADY_OPEN` | The payment already has an open recall (its id is in `message`). Only one recall per payment may be open, because PAPSS's answers do not all carry the recall id. After a final rejection you may recall again. |
| `422` | `PAPSS_VALIDATION_FAILED` | Missing/invalid `reason`, `recallId` or payment reference. |

How it proceeds (states in `papssOutcome` of the recall):

1. `RECALL_PENDING`: stored with the signed camt.056 before it is submitted. An ambiguous submission shows `status: "UNKNOWN"`: re-send the same request with the same `recallId`.
2. PAPSS answers at once: `RECALL_ACCEPTED_BY_PAPSS` (ACCP). This means **accepted for processing, not completed**: the money has not moved and your payment is still `SETTLED`. Or `RECALL_REJECTED_BY_PAPSS` (RJCT, the reason code in `reasonCode`, final).
3. The beneficiary bank answers (PAPSS allows it 30 days; `deadlineAt` on the recall, `responseOverdue: true` after it, record-only):
   - funds returned: a pacs.004 arrives; your payment becomes `RETURNED` (the usual [return callback](#return-received-from-papss)) and the recall `RECALL_RETURNED` (final);
   - refused: a camt.029; the recall becomes `RECALL_REJECTED_BY_BENEFICIARY` (final, with the reason, for example `CUST`, `AGNT`, `LEGL`).

Each answer is pushed on the [recall result callback](#recall-result-callback). Follow a recall with `GET /api/v1/Gateway/Recall/{recallId}` (summary `status`: `PENDING` until returned or rejected, `COMPLETED` when returned, `REJECTED`, `UNKNOWN`), and on the payment lookup (`recalls`). The recall lookup shows `originalRequestMessageId`, `originalTxId`, `originalEndToEndId`, `originalPaymentOutcome`, `reason` (yours), `statusReasonCode` (the answer's reason), `returnId`, `deadlineAt`, `responseOverdue` and the `statusHistory` of the answers. PAPSS has no recall status enquiry.

Fees: PAPSS confirmed that a recall returned within 7 days of settlement gives back exactly the original amount. A different amount is noted on the recall's history, never blocked; beyond 7 days the fee treatment is not established by PAPSS.

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
| `404` | Recall: the payment to recall is not stored (`ORIGINAL_PAYMENT_NOT_FOUND`); lookups: `OPERATION_NOT_FOUND` | Check the references |
| `409` | Recall refused in the payment's current state (`ORIGINAL_NOT_SETTLED`, `RECALL_WINDOW_EXPIRED`, `RECALL_ALREADY_OPEN`) | Do not retry unchanged; follow the open recall or wait for settlement |
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

### Payment and return status callback

Every PAPSS status that advances one of your payments or returns is pushed with the mapping `<profile>.CB_CompletionNotification` (baseline):

```json
{ "txId": "<ORIGINAL_PAYMENT_TX_ID>", "endToEndId": "<END_TO_END_ID>", "status": "ACSC", "reason": null, "additionalInfo": null }
```

- `status` is the ISO status: `ACCP` or `ACSP` (accepted, not final), `ACSC` (settled, final), `RJCT` (rejected, final, `reason` carries the code), `PDNG` (pending).
- Several callbacks may arrive for one payment, for example `ACSP` then `ACSC`. The order and number are not fixed by PAPSS; SIPS Connect never sends a status that would move a payment backwards and never sends a second, different final status.
- Headers: `X-Idempotency-Key: <txId>:<status>` (a return uses `<returnId>:<status>`), `X-Transaction-Id`, `X-Papss-Operation` (`PAYMENT` or `RETURN`), `X-Return-Id` for a return, and `X-Papss-Source-Message-Id`.
- For a return, `txId` is the **original payment's** TxId (PAPSS reports it that way); use `X-Return-Id` to tell the return's result from the payment's.
- A status for a payment received by your bank (INBOUND) is pushed the same way.
- Delivery works like the verification result callback: stored first, pushed from an outbox with retries, at-least-once.

### Return received from PAPSS

When a payment you sent is returned, PAPSS delivers a pacs.004. SIPS Connect stores it, marks your payment `RETURNED` and pushes it with `<profile>.CB_ReturnRequest`:

```json
{ "txId": "<YOUR_ORIGINAL_TX_ID>", "endToEndId": "<YOUR_END_TO_END_ID>", "agent": "<RETURNING_AGENT>", "reason": "<RETURN_REASON>", "additionalInfo": "<TEXT>", "returnId": "<RTR_ID>" }
```

Headers: `X-Idempotency-Key: <returnId>`, `X-Return-Id`, `X-Transaction-Id`. The return is pushed even if SIPS Connect does not hold the original payment. No pacs.002 is sent back to PAPSS for a received return.

### Recall result callback

Every answer to one of your recalls is pushed with the mapping `<profile>.CB_RecallResult` (baseline):

```json
{
  "recallId": "SIPS-0123456789abcdef01234567",
  "txId": "<YOUR_ORIGINAL_TX_ID>",
  "endToEndId": "<YOUR_ORIGINAL_END_TO_END_ID>",
  "outcome": "RECALL_ACCEPTED_BY_PAPSS",
  "reasonCode": null,
  "responderId": "<PAPSS StsId, camt.029 CxlStsId or pacs.004 RtrId>",
  "sourceMessageId": "<PAPSS source message id>",
  "receivedAt": "2026-09-26T08:00:01.020Z"
}
```

- `outcome`: `RECALL_ACCEPTED_BY_PAPSS` (not final), `RECALL_REJECTED_BY_PAPSS`, `RECALL_REJECTED_BY_BENEFICIARY` or `RECALL_RETURNED` (final). A recall normally produces two callbacks: PAPSS's answer, then the beneficiary's.
- Headers: `X-Idempotency-Key: <recallId>:<outcome>`, `X-Recall-Id`, `X-Transaction-Id`, `X-Papss-Operation: RECALL`, `X-Papss-Source-Message-Id`.
- PAPSS's answer to a recall carries your payment's TxId/EndToEndId, but it is **not** a payment status: no `CB_CompletionNotification` is sent for it and your payment's state does not change. When the funds come back you receive both `CB_ReturnRequest` (the pacs.004, as for any return) and `CB_RecallResult` with `RECALL_RETURNED`.
- An answer SIPS Connect cannot attribute to a recall is stored for SPS operations (`Operations/Unresolved`) and not pushed.
- Delivery works like the other callbacks: stored first, pushed from an outbox with retries, at-least-once.

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
PaymentResponse
StatusRequest
ReturnRequest
PapssAdmissionResponse
OperationResult
ReadinessRequest
ReadinessResponse
ParticipantDiscoveryRequest
ParticipantDiscoveryResponse
FxRequest
FxResponse
```

Callback mappings used on the PAPSS profile: `CB_VerificationRequest`, `CB_VerificationResponse`, `CB_VerificationResult`, `CB_PaymentRequest`, `CB_PaymentResponse`, `CB_CompletionNotification` and `CB_ReturnRequest`.

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
7. Exercise Verify, Payment, Status, Return, Recall, Readiness, Discovery, and FX.
8. Confirm signed-ISO correlation and asynchronous callback delivery where applicable.
9. Test duplicate callback handling, ambiguous delivery reconciliation, denied operations, stale/ambiguous/ineligible directory observations, unsupported currencies/instruments, and credential failure.
10. Record request references, expected results, timestamps, and evidence without recording secrets or sensitive account data.
