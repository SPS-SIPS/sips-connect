# PAPSS operation store: configuration reference

SIPS Connect records every PAPSS operation it takes part in, in PostgreSQL, before it acknowledges anything. The same database also serves as the outbox for bank callbacks and for replies to the WP-SIPS gateway. This page lists the settings, the database objects, the states and the PAPSS rules that are still open. For the bank-facing behaviour, see [PAPSS_BANK_INTEGRATION_GUIDE.md](PAPSS_BANK_INTEGRATION_GUIDE.md).

## Settings

Every setting is optional. With all of them unset, SIPS Connect behaves as it did before the store existed: it computes no deadline, never expires or purges records and never holds a reply. You can set each one in `appsettings.json` under `PapssFacing`, as an ASP.NET environment variable (`__` separator), or through the `.env` variable that `docker-compose*.yml` passes through.

The **Source** column says where a default comes from:
- **PAPSS**: a value PAPSS publishes.
- **SPS-internal**: an engineering default chosen by SPS. It is not a PAPSS protocol rule.
- **UNRESOLVED**: PAPSS has not settled this rule (see [Unresolved PAPSS behaviour](#unresolved-papss-behaviour)).

| Setting (environment variable) | `.env` variable | Default | Source | Meaning |
| --- | --- | --- | --- | --- |
| `PapssFacing__Inbound__Acmt023__ResponseDeadlineSeconds` | `PAPSS_ACMT023_RESPONSE_DEADLINE_SECONDS` | unset | UNRESOLVED | How long after the clock below the reply to an inbound acmt.023 is due. Only recorded (`deadline_at`, logs, lookups, metric). Set it together with `DeadlineClock`, or leave both unset. |
| `PapssFacing__Inbound__Acmt023__DeadlineClock` | `PAPSS_ACMT023_DEADLINE_CLOCK` | unset | UNRESOLVED | `SourceCreationTime` (AppHdr `CreDt` of the acmt.023) or `ReceivedAt` (when SIPS Connect received it). If this is unset, no deadline is computed. |
| `PapssFacing__Inbound__LateResponsePolicy` | `PAPSS_LATE_RESPONSE_POLICY` | `Submit` | UNRESOLVED (default keeps current behaviour) | `Submit`: send the reply even when it is late, and log a warning and increment `sips_papss_reply_submitted_after_deadline_total`. `Hold`: do not submit a reply that is already past its recorded deadline. The reply is kept in state `HELD` for operators. |
| `PapssFacing__Outbound__VerificationResultExpirySeconds` | `PAPSS_VERIFICATION_RESULT_EXPIRY_SECONDS` | unset (never) | SPS-internal / no PAPSS value | Lookups report an outbound verification that is still `PENDING` after this many seconds as `EXPIRED`. Nothing is sent to PAPSS. A result that arrives later still completes the operation. Lookups always return `ageSeconds`. |
| `PapssFacing__Store__RetentionDays` | `PAPSS_STORE_RETENTION_DAYS` | unset (never purge) | SPS-internal / no PAPSS value | When set, the retention job deletes completed operations older than this many days. It also deletes events older than this many days that are no longer waiting to be pushed. It never deletes pending work. |
| `PapssFacing__Store__PurgeIntervalMinutes` | `PAPSS_STORE_PURGE_INTERVAL_MINUTES` | `60` | SPS-internal | How often the retention job runs, when `RetentionDays` is set. |
| `PapssFacing__Delivery__MaxAttempts` | `PAPSS_DELIVERY_MAX_ATTEMPTS` | `10` | SPS-internal | Maximum attempts for each bank push and each gateway reply. After the last attempt the item becomes `FAILED`. |
| `PapssFacing__Delivery__InitialBackoffSeconds` | `PAPSS_DELIVERY_INITIAL_BACKOFF_SECONDS` | `5` | SPS-internal | Wait before the first retry. The wait then doubles for each further attempt. |
| `PapssFacing__Delivery__MaxBackoffSeconds` | `PAPSS_DELIVERY_MAX_BACKOFF_SECONDS` | `300` | SPS-internal | Upper limit on the wait between retries. |
| `PapssFacing__Delivery__PollIntervalSeconds` | `PAPSS_DELIVERY_POLL_INTERVAL_SECONDS` | `5` | SPS-internal | How often the outbox workers check for due work. New work also wakes them immediately. |
| `PapssFacing__Delivery__ClaimLeaseSeconds` | `PAPSS_DELIVERY_CLAIM_LEASE_SECONDS` | `120` | SPS-internal | How long one instance holds a claimed item. After that, another instance may retry the item, for example after a crash. |
| `PapssFacing__Lookup__MaxWaitSeconds` | `PAPSS_LOOKUP_MAX_WAIT_SECONDS` | `0` (disabled) | SPS-internal | Maximum `?waitSeconds=` long-poll on the lookup API, from 0 to 300. |
| `PapssFacing__Status__EnquiryMinimumAgeSeconds` | `PAPSS_STATUS_ENQUIRY_MIN_AGE_SECONDS` | unset | UNRESOLVED (NOT ESTABLISHED) | `/Status` on the PAPSS rail sends a pacs.028 only for our own non-final payment. When this is set, a payment younger than this many seconds is answered from the store without a pacs.028. Unset keeps the previous behaviour: enquire whenever the payment is non-final. PAPSS only names the triggers (no pacs.002 received, stuck pending, confirming finality; evidence §E11). |
| `PapssFacing__Returns__SettledStatuses` | `PAPSS_RETURN_SETTLED_STATUSES` | `ACSC` | UNRESOLVED (CONTRADICTED) | Comma-separated pacs.002 statuses that settle an outbound return and mark the original payment `RETURNED`. Allowed: `ACSC`, `ACSP`. The portal flow says ACSC, the pacs.004 response sample says ACSP (evidence §D10). |

For the core-bank call that answers an inbound acmt.023, SIPS Connect uses the existing `Core:CoreBankTimeoutSeconds` setting (default `3`), so behaviour there is unchanged. The WP-SIPS request timeout is still `PapssFacing:RequestTimeoutSeconds`.

Validation at start-up: positive values where a number is set; the deadline value and clock set together; `MaxBackoffSeconds >= InitialBackoffSeconds`; `ClaimLeaseSeconds >= 10`; `Lookup:MaxWaitSeconds` from 0 to 300; `Returns:SettledStatuses` lists only `ACSC`/`ACSP` and is not empty. An empty environment variable means "unset" (the compose files default `PAPSS_RETURN_SETTLED_STATUSES` to `ACSC`).

## Database objects

The migrations are `20260924234525_AddPapssOperationStore` (Phase 1, verification) and `20260925050858_AddPapssPaymentOperations` (Phase 2, payments, returns and status enquiries; additive, nullable columns only). The tables are separate from `isomessages`. Column names follow the repository's lower-case naming convention.

- `papss_operations`: one row per operation, in either direction.
  - Key: `(direction, requestmessageid)` is unique.
    - For OUTBOUND rows, `requestmessageid` is the `requestMessageId` returned to the bank.
    - For INBOUND rows, it is the PAPSS source message id (the AppHdr `BizMsgIdr` of the acmt.023).
  - Business keys: `verificationid`, `endtoendid`, `txid`, `counterpartybic`, `accountid`, `accounttype`.
  - State dimensions (see [States](#states)): `gatewaystate`, `papssoutcome`, `bankdeliverystate`.
  - Result: `verified`, `accountname`, `currency`, `reason`, `additionalinfo`, `admissioncode`, `reasoncode`.
  - Signed messages: `signedrequest` and `signedresponse` (bytea).
  - Times: `sourcecreatedat`, `receivedat`, `createdat`, `updatedat`, `completedat`, `deadlineat`.
  - Row version: `xmin`.
  - Payment columns (Phase 2):
    - `msgid`: GrpHdr MsgId of the pacs.008/pacs.004/pacs.028 we sent, which the gateway echoes as pacs.002 `OrgnlMsgId`; for INBOUND rows, the MsgId of the message received.
    - `returnid`; `originaloperationid` (self link: return -> payment, status enquiry -> payment, `ON DELETE SET NULL`); `originaltxid`, `originalendtoendid`.
    - `amount` (numeric(18,5)), `currency`, `localinstrument`.
    - `paymentstatus`: raw ISO status in effect (`ACCP`, `ACSP`, `ACSC`, `PDNG`, `RJCT`); `statusreasoncode` (the pacs.002 `StsRsnInf/Rsn`, or the pacs.004 return reason); `statusat`.
    - `statusconflict`: a different final status arrived after a final one (operator attention).
    - `requestfingerprint`: SHA-256 of the bank request content, for TxId / ReturnId idempotency.
    - `isomessageid`: INBOUND payment only, the `isomessages` row with the bank decision and the PAPSS decision outbox.
  - Unique: `ux_papss_op_payment_txid` on `(direction, operation, txid)` where `operation = 'PAYMENT'`; `ux_papss_op_return_id` on `(direction, returnid)` where `operation = 'RETURN'`.
- `papss_operation_events`: an append-only log of received messages.
  - Each event is unique per `(eventtype, sourcemessageid)`, so a redelivered callback is stored once.
  - Event types: `VERIFICATION_RESULT` (acmt.024), `VERIFICATION_ENQUIRY` (acmt.023), `PAYMENT_STATUS` (pacs.002), `PAYMENT_RECEIVED` (pacs.008), `RETURN_RECEIVED` (pacs.004).
  - Each event carries its bank push outbox columns: `pushstate`, `pushattempts`, `pushnextattemptat`, `pushlasterror`, `pushdeliveredat`.
  - Status history columns (payment events): `status`, `reasoncode`, `correlation` (`MSG_ID`, `TX_ID`, `NONE`, `MISMATCH`), `disposition` (`APPLIED`, `NOT_ADVANCING`, `DUPLICATE_FINAL`, `CONFLICT`, `UNKNOWN_STATUS`, `UNCORRELATED`), `originalmessageid`, `originalmessagetype`, `originaltxid`, `originalendtoendid`, `amount`, `currency`, `note`. An event with `operationid IS NULL` was not attached to any operation.
  - Provenance columns (pacs.002 / pacs.004 events, migration `AddPapssEventProvenance`), from the gateway's signed `SplmtryData/Envlp/PapssProvenance` (`urn:sps:papss:provenance:001`, schema `docs/schemas/SPS.PAPSS.PROVENANCE.001.xsd`): `rawevidencereference` (SHA-256 of the raw signed PAPSS message the gateway keeps; with `sourcemessageid` it links the event to the network evidence), `amountsource` and, for pacs.004, `categorypurposesource` (`NETWORK_REPORTED`, `LOCAL_RECONSTRUCTION`, or `UNSPECIFIED_LEGACY` when the gateway sent no provenance), and `fieldprovenance` (JSON map of element path to source, also `IDENTIFIER_TRANSLATION` and `DEFAULT_FILLER`). A `LOCAL_RECONSTRUCTION` amount was supplied by the gateway from the original payment, not reported by PAPSS: it is stored on the event but never compared with, or copied into, the operation amount (the event note says so). Rows written before the migration keep NULL.
- `papss_outbound_responses`: the outbox of signed replies bound for the gateway.
  - Unique by `bizmsgidr` and by `operationid`, so there is one reply per operation.
  - Columns: `state`, `attempts`, `nextattemptat`, `lasterror`, `admissioncode`, `submittedat`, `admittedat`.
  - Rows are deleted together with their operation.

The existing `isomessages` `VerificationRequest` row is still written for answered inbound PAPSS enquiries. This is best-effort and only for audit and dashboards; the operation store is the authoritative record.

## States

| Dimension | Values | OUTBOUND (bank verifies abroad) | INBOUND (abroad verifies our account) |
| --- | --- | --- | --- |
| `gatewaystate` | `NOT_SUBMITTED`, `SUBMITTING`, `ADMITTED`, `REJECTED`, `SUBMISSION_UNKNOWN` | Technical admission (admi.002) of our acmt.023 | Admission of our acmt.024 reply |
| `papssoutcome` | `PENDING`, `VERIFIED_MATCH`, `VERIFIED_NO_MATCH`, `REJECTED`, `UNKNOWN` | Result that PAPSS returned | Answer that we gave (`UNKNOWN` = core bank did not answer) |
| `bankdeliverystate` | `NOT_REQUIRED`, `PENDING`, `DELIVERED`, `FAILED` | Push of the result to the bank callback | Whether the core bank answered |

Payments, returns and status enquiries use the same three columns:

| Dimension | PAYMENT / RETURN OUTBOUND | PAYMENT INBOUND | RETURN INBOUND |
| --- | --- | --- | --- |
| `gatewaystate` | admi.002 admission of our pacs.008 / pacs.004 (/pacs.028 for `STATUS_ENQUIRY`) | State of our PAPSS decision (pacs.002) in the `isomessages` decision outbox: `SUBMITTING` queued, `ADMITTED` published, `REJECTED` terminal failure | `NOT_SUBMITTED` (nothing is sent back) |
| `papssoutcome` (payment outcome) | `PENDING`, `ACCEPTED` (ACCP/ACSP), `SETTLED` (ACSC), `REJECTED` (RJCT, or gateway rejection), `RETURNED` (a return settled against it), `UNKNOWN` | Our decision (`ACCEPTED` for ACCP, `REJECTED` for RJCT), then a later PAPSS final status if one is delivered; `RETURNED` when we return it | `SETTLED` on receipt |
| `bankdeliverystate` | Push of the latest applied status to the bank callback | First whether the core bank answered `CB_PaymentRequest` (`DELIVERED` once the decision is stored), then the push of a later PAPSS status | Push of `CB_ReturnRequest` |

`SETTLED`, `REJECTED` and `RETURNED` are final. How statuses are applied:
- Every received pacs.002 is stored as an event, de-duplicated on its AppHdr `BizMsgIdr`.
- A status is applied only when it advances the payment: `PDNG` < `ACCP` < `ACSP` < final. A late lower status is kept as `NOT_ADVANCING` history and not pushed.
- A final state is never regressed. The same final status again is `DUPLICATE_FINAL` (not pushed again). A different final status is `CONFLICT`: the first final status is kept, `statusconflict` is set and an error is logged.
- A code outside `ACCP`/`ACSP`/`ACSC`/`PDNG`/`RJCT` is kept as `UNKNOWN_STATUS` and not applied.
- When an outbound return settles (`Returns:SettledStatuses`), its original payment becomes `RETURNED`. A received pacs.004 marks our payment `RETURNED` directly. A return against a `REJECTED` (or already `RETURNED`) payment is flagged, not applied.
- A status for a payment also completes any open `STATUS_ENQUIRY` linked to it.

The lookup API adds one summary `status`:
- `PENDING`
- `COMPLETED`
- `REJECTED`
- `FAILED`: inbound only. The core bank did not answer, or the reply was held.
- `UNKNOWN`: the submission outcome is ambiguous, or the reply retries ran out.
- `EXPIRED`: only when `VerificationResultExpirySeconds` is set (verification only).

For payments and returns: `COMPLETED` = `SETTLED` or `RETURNED`; `REJECTED` = rejected by PAPSS, by us (inbound decision RJCT) or by WP-SIPS; `UNKNOWN` = submission ambiguous and no status yet; otherwise `PENDING` (including `ACCEPTED`).

## Payment correlation (pacs.002 from the gateway)

The gateway delivers every payment, return and status-enquiry result as a signed pacs.002.001.12 on `/api/v1/Incoming`. Only messages that pass the PAPSS callback guard take this path; the SmartVista handlers are not involved (no orphan reject).

1. Primary key: `OrgnlMsgId` against the stored `msgid` (or `requestmessageid`). `OrgnlMsgNmId` selects the kind: `pacs.008.*` = payment, `pacs.004.*` = return; missing or other = payment first, then return.
2. The match is only attached when `OrgnlTxId` and `OrgnlEndToEndId` equal the stored values. For a return these are the *original payment's* TxId/EndToEndId (evidence §B4), compared with `originaltxid`/`originalendtoendid`. A message-id match with different TxId/EndToEndId is stored as `MISMATCH`, not attached, and logged.
3. Secondary key, only when no message id matched: `OrgnlTxId` + `OrgnlEndToEndId` (payments in either direction, outbound first; outbound returns when exactly one matches). This is the key PAPSS documents (evidence §B4).
4. Nothing matched: stored with `operationid` NULL and disposition `UNCORRELATED`, not pushed, logged; the callback is still acknowledged (2xx after commit). Operators list these with `GET /api/v1/Gateway/Operations/Unresolved`.

Amount and currency are taken from the stored operation; a reported amount/currency that differs is recorded in the event `note`.

Inbound pacs.004: linked to our OUTBOUND payment by `OrgnlTxId` + `OrgnlEndToEndId`, de-duplicated on the source `BizMsgIdr` and on `RtrId` (a repeated `RtrId` under a new source id is kept for audit only). It is always pushed to the bank (`CB_ReturnRequest`), even when the payment is unknown.

Inbound pacs.008: the existing handler still records `isomessages`, calls the core bank (`CB_PaymentRequest`) and stores the decision; the existing decision outbox still publishes the signed pacs.002 and retries it. The operation store records an INBOUND `PAYMENT` (de-duplicated on source `BizMsgIdr` and TxId) and mirrors the decision and the outbox state (`decisionState` on the lookup).

## Idempotency of outbound payments and returns

The bank's `txId` (payments) and `returnId` (returns) are the idempotency keys:
- New key: the message is built and signed, stored with its signed bytes and a fingerprint of the bank request, then submitted.
- Same key, same content: an `ADMITTED` operation returns its stored admission; a `REJECTED` one returns its stored rejection code; a `SUBMITTING`/`SUBMISSION_UNKNOWN` one re-submits the stored signed bytes unchanged (the gateway answers `EXACT_REPLAY`). Directory discovery/readiness is not repeated.
- Same key, different content: `DUPLICATE_CONFLICT` (HTTP 400) without calling WP-SIPS or PAPSS.
- Validation or directory failures happen before anything is stored, so the bank may correct and retry with the same key.

## Workers

All state lives in the database. After a restart, the workers carry on with whatever is pending. More than one instance can run safely: an instance claims an item with a compare-and-set on `(state, attempts)`, and the claim is held for the lease period. Delivery is at-least-once. The bank de-duplicates on `X-Idempotency-Key` (the verification id). The gateway de-duplicates the byte-identical reply and answers `EXACT_REPLAY`.

- `PapssBankPushWorker`: delivers events whose `pushstate` is `PENDING` to the bank, using the configured `CallbackMappingProfile` and `CallbackUrl`:
  - `VERIFICATION_RESULT` -> `CB_VerificationResult` (`X-Idempotency-Key` = verification id).
  - `PAYMENT_STATUS` -> `CB_CompletionNotification` (`X-Idempotency-Key` = `<txId>:<status>`, or `<returnId>:<status>` for a return, so ACSP and a later ACSC are both delivered; plus `X-Transaction-Id`, `X-Return-Id`, `X-Papss-Operation`, `X-Papss-Source-Message-Id`).
  - `RETURN_RECEIVED` -> `CB_ReturnRequest` (`X-Idempotency-Key` = `RtrId`).
  - A worker has no request context, so it re-establishes the mapping profile and callback URL from configuration.
- `PapssResponseOutboxWorker`: submits replies whose state is `PENDING` to `PapssFacing:IsoIngressUrl`, re-sending the stored bytes unchanged.
  - `RECEIVED_AND_DURABLY_ADMITTED` or `EXACT_REPLAY`: the reply becomes `ADMITTED`.
  - Any other admi.002 code: the reply becomes `REJECTED`. This is terminal, and the code is stored.
  - A transport error, a timeout or a response that cannot be verified is treated as ambiguous: the reply is retried and the operation's gateway state becomes `SUBMISSION_UNKNOWN`.
- `PapssStoreRetentionWorker`: runs only when `RetentionDays` is set.

## Unresolved PAPSS behaviour

The following PAPSS rules are not settled. SIPS Connect does not invent them, so every related setting defaults to "off".

1. **Authoritative clock for response deadlines.** PAPSS has not said whether the source `CreDt` or the receipt time counts. Both are available, and you choose with `DeadlineClock`.
2. **Timeout values per message type.** No value is established for acmt.023. The pacs.008 material contradicts itself (20 s against 120 s). Any value you configure is provisional.
3. **Negative reply when the core bank times out.** It is not established whether a participant may send `Vrfctn=false` when its core bank fails or times out, or with which reason code. SIPS Connect therefore sends **no** acmt.024 in that case. It records `bankdeliverystate=FAILED` and `papssoutcome=UNKNOWN`, and PAPSS applies its own timeout.
4. **Reason codes in acmt.024.** The PAPSS material contradicts itself: one source has a numeric 1000–1010 table, another uses ISO codes such as `MS03` and `AC01`. It is also unclear whether a reason is mandatory when `Vrfctn=false`. SIPS Connect puts in `Rpt/Rsn/Prtry` exactly the reason the core bank returned. If the core bank returned none, or a value that is not a valid Max35Text, the reply has **no** `Rsn` and a warning is logged. It never substitutes a default such as `MISS` or `SUCC`. `AdditionalInfo` has no element in acmt.024.001.03; it is kept in the store only.
5. **Suppressing late responses.** PAPSS has not said whether a late reply must be suppressed. The default is `Submit`; `Hold` is available when an operator needs it.
6. **Expiry, retention and clock skew.** PAPSS publishes no value for any of these, so they are unset.

Payments, returns and status enquiries (Phase 2). The authoritative review with citations is `art/papss/evidence/phase2-evidence-20260925.md`; section references below point into it.

7. **Number and order of pacs.002 per payment** (§B5, CONTRADICTED): one final pacs.002, a synchronous ACSP then the final one, or a synchronous final ACSC. The store therefore accepts any sequence and only applies advancing statuses (see [States](#states)). SL1016 is `nonInstant` in UAT; what that does to the sequence is NOT ESTABLISHED.
8. **Return result status** (§D10, CONTRADICTED): ACSC vs ACSP. Configurable with `Returns:SettledStatuses` (default `ACSC`).
9. **pacs.028 timing and answerer** (§E11): any minimum age / rate is NOT ESTABLISHED (`Status:EnquiryMinimumAgeSeconds`, default unset); whether PAPSS or the receiving participant answers is CONTRADICTED. SIPS Connect does not depend on it: the answer is an ordinary pacs.002 correlated to the payment.
10. **Amounts in PAPSS RJCT** (§B4, NOT ESTABLISHED): amounts are optional in the correlation; the stored amount is authoritative.
11. **Beneficiary reject reason codes** (§C8, NOT ESTABLISHED) and **pacs.004 reason codes** (§D10, FOCR vs DUPL, CONTRADICTED): reason codes are passed through unchanged, never defaulted.
12. **Inbound pacs.004 finality** (§D10): an inbound pacs.004 is acknowledged, not answered with a pacs.002, so it is recorded as `SETTLED` on receipt and its payment as `RETURNED`. Whether spontaneous returns (without a camt.056) are allowed is CONTRADICTED; SIPS Connect records whatever PAPSS delivers.
13. **pacs.004 OrgnlMsgId convention** (§D10, CONTRADICTED) and **UETR** (§A2): not used for correlation.

## Known limitations

- An inbound enquiry can stay at `bankdeliverystate=PENDING` indefinitely. This happens when the process crashes after the enquiry was stored and before the core bank answered. A redelivery from PAPSS deliberately does not call the core bank again. The row is visible through the lookup API.
- An outbound verification left at `SUBMISSION_UNKNOWN` is not re-submitted automatically. When the bank calls `/Verify` again with the same `MsgId`, SIPS Connect re-submits the stored signed bytes. A result that arrives from PAPSS also settles it.
- An outbound payment or return left at `SUBMISSION_UNKNOWN` is not re-submitted automatically either: the bank re-sends the same `txId` / `returnId` (re-submits the stored bytes), or a PAPSS status settles it.
- A pacs.002 whose correlation keys are unknown to the store (for example a payment submitted before Phase 2 was deployed) is stored `UNCORRELATED` and is **not** pushed to the bank; it needs operator reconciliation (`Operations/Unresolved`). Evidence §G shows no pacs traffic in UAT so far.
- A conflicting second final status is not pushed to the bank; operators resolve it from the lookup (`statusConflict`, `statusHistory`).
- An inbound payment whose PAPSS final status never arrives stays `ACCEPTED` (not `COMPLETED`), so the retention job never purges it.
- The gateway (not SIPS Connect) builds the PAPSS pacs.008/pacs.004/pacs.028 wire messages; the structural gaps listed in evidence §A2, §C9, §D10 and §E11 are gateway work.

## Running the PostgreSQL tests

`SIPS.Connect.PostgresTests` needs a real PostgreSQL server. It is not part of `SIPS.sln`, so normal unit runs are unaffected. When `SIPS_TEST_POSTGRES_CONNECTION` is not set, every test fails with an explicit message. Each test creates, migrates and drops its own database, so the connection string needs permission to create databases.

The following commands start PostgreSQL, run the tests in the .NET SDK container, and stop PostgreSQL:

```sh
docker network create sips-test || true
docker run -d --rm --name sips-test-pg --network sips-test -e POSTGRES_PASSWORD=test -e POSTGRES_DB=sips postgres:16
docker run --rm --network sips-test -v "$PWD":/src -w /src -e DOTNET_ROLL_FORWARD=Major \
  -e SIPS_TEST_POSTGRES_CONNECTION="Host=sips-test-pg;Database=sips;Username=postgres;Password=test" \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet test SIPS.Connect.PostgresTests/SIPS.Connect.PostgresTests.csproj
docker stop sips-test-pg
```

With a local SDK and a local PostgreSQL server:

```sh
SIPS_TEST_POSTGRES_CONNECTION="Host=localhost;Database=sips;Username=postgres;Password=test" \
  dotnet test SIPS.Connect.PostgresTests/SIPS.Connect.PostgresTests.csproj
```
