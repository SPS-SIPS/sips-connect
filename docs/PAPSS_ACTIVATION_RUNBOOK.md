# SIPS Connect — PAPSS activation runbook

Version: 1.0 | Prepared: 9 November 2026 | Source baseline: SIPS checkout `ee4346e`

Audience: participant banks, SIPS Connect operators, core banking teams, treasury, security, support and reconciliation teams.

Status: operational template grounded in the current implementation. Complete the bank/environment-specific fields and obtain the release sign-offs before production activation. UAT evidence and production approval are not implied by this document.

## 1. What activation means

PAPSS activation adds a second payment rail to an existing SIPS Connect bank deployment. Domestic SIPS continues through the SmartVista/IPS path. PAPSS requests use the configured PAPSS WP-SIPS service and its separate signing profile.

The bank sends authenticated JSON over HTTPS to SIPS Connect. SIPS Connect maps the JSON, constructs and signs ISO 20022 messages, verifies responses, stores operations and delivers bank callbacks. The bank application does not build ISO messages, sign WP-SIPS envelopes or call the PAPSS `/sips/messages` ingress directly.

Each SIPS Connect deployment represents one bank. `Xades:BIC` is its protected sender identity; a user or request body cannot choose another sending bank.

SPS acts only as the technical connector participant. Its activation scope covers connectivity, identity and trust, routing, message processing, operation records, callbacks and technical reconciliation. SPS does not own, operate, approve or reconcile settlement accounts, pre-funding, liquidity or settlement obligations. Those responsibilities belong to the participant bank and its PAPSS or sponsor arrangements.

SPS technical activation requires a validated deployment, successful bank integration tests and operational support readiness. The bank separately owns its business launch decision and any settlement prerequisites; these are not SPS technical sign-off responsibilities.

## 2. Activation record and accountability

Complete and attach this record to the change ticket:

| Item | Required value/evidence | Owner |
|---|---|---|
| Participant | Legal bank name, local BIC, country, PAPSS participant reference | Bank / PAPSS onboarding |
| Participation model | Direct or indirect; approved sponsor where applicable | Bank treasury |
| Environment and release | UAT/production, SIPS build, gateway build, mapping version, change ticket | Release manager |
| Enabled scope | Outbound/inbound payments, verification, currencies, instruments, returns, recalls, cross-currency | Product / operations |
| Network | Approved URLs, DNS, firewall rules and transport authentication | Infrastructure |
| Trust | Certificate fingerprints, issuer/serial, validity dates and registration evidence | Security |
| Bank-only business readiness | Bank confirmation of its PAPSS/sponsor arrangements; retain settlement account and liquidity details within bank processes | Participant bank |
| Operations | Support roster, escalation contacts, service hours, agreed response deadlines | Operations |
| Technical approval | UAT report, technical security approval and connector activation authorization | SPS technical release owner |
| Bank launch approval | Bank business and financial readiness under its own procedures | Participant bank |

The bank owns core banking decisions and ledger postings. SIPS operations owns routing, mappings, operation storage and delivery workers. The PAPSS/gateway operator owns remote enrollment, directory/capability publication and remote service availability. The participant bank treasury owns liquidity and settlement reconciliation; SPS has no settlement role. Assign named primary and backup contacts for each.

## 3. What the bank needs before activation

### 3.1 Participant bank responsibilities outside SPS technical scope

This subsection provides bank onboarding context only. The bank manages these matters directly with PAPSS, its sponsor and its own treasury. SPS does not collect settlement account details, manage funds or approve settlement readiness.

- Confirm PAPSS enrollment and the permitted countries, currencies, payment schemas and services for this bank and environment.
- Confirm the participation model. PAPSS describes direct participation with settlement-account requirements and indirect participation through a sponsoring direct participant. Obtain the applicable agreement and onboarding acceptance rather than assuming technical connectivity establishes membership. [PAPSS participation](https://papss.com/get-connected/).
- Establish the applicable pre-funding, funding/defunding and settlement arrangements. PAPSS describes pre-funding as a prerequisite to supporting the movement of payments; indirect participants fund through their sponsor. Confirm the actual model and accounts with PAPSS/treasury for this deployment. [PAPSS funding model](https://papss.com/how-it-works/).
- Approve transaction and daily limits, supported customer/account types, customer fees, FX disclosure, screening and exception ownership according to the bank's applicable obligations and agreements.
- Define when customer funds are reserved, debited, credited or released for each state. Explicitly cover uncertain submissions, delayed final statuses, rejections, returns and callback failures.
- Agree settlement-report sources, reconciliation frequency, discrepancy escalation and end-of-day ownership. Network payment completion and treasury settlement reconciliation are separate controls.

### 3.2 Technical information exchange

Exchange through the approved secure onboarding channel:

- Local `Xades:BIC`, local country and permitted sending currencies.
- SIPS Connect bank-facing base URL and credentials: a JWT principal with `Gateway` role, or configured API key/secret with the required role.
- PAPSS WP-SIPS ingress URL, exact allowed host, remote WP-SIPS identity, environment and business/security profile.
- Participant signing certificate, public certificate chain and required registration metadata. Keep the private key and passphrase in the participant deployment's secret store.
- Approved remote responder certificate/trust metadata and rotation procedure.
- Reachable SIPS Connect signed inbound URL: `/api/v1/Incoming`, as exposed through the approved network route.
- Bank notification HTTPS callback URL, callback authentication and mapping profile.
- Core bank verification and transfer endpoints, credentials, request/response mappings and agreed timeout handling.
- TLS and any mTLS requirements for each hop. PAPSS adapter transport/signing certificates are not automatically bank configuration values.

### 3.3 Deployment and storage readiness

- Deploy the approved release with PAPSS disabled initially.
- Apply the database migrations required by that release, including the PAPSS operation/event, decision outbox, payment, recall and inbound recall changes where in scope. Follow the normal deployment migration procedure.
- Verify database permissions, storage capacity, backups, restore procedure and consistent access from all SIPS instances.
- Validate signing-key access, certificate discovery/trust services, synchronized system clocks, TLS validation, DNS and firewall connectivity in both directions.
- Load the bank's JsonAdapter mappings and PAPSS callback profile on every node. Preserve required `InternalField` names when customizing bank-facing `UserField` names.
- Prove core-bank and notification receivers are idempotent. A lost HTTP response must not cause a second ledger posting.

## 4. Configuration checklist

| Setting | Activation requirement |
|---|---|
| `Xades:BIC` | Exact approved local bank BIC; protected deployment identity |
| Existing `Xades` / `Core` configuration | Approved signing key, certificate chain, algorithms, discovery/trust settings and bank integration |
| `PapssFacing:Enabled` | `false` during preparation; `true` only for the approved activation scope |
| `PapssFacing:IsoIngressUrl` | Approved absolute PAPSS WP-SIPS ingress URL |
| `PapssFacing:AllowedHosts` | Explicitly includes the ingress hostname |
| `PapssFacing:Environment` | Agreed environment identifier |
| `PapssFacing:RemoteWpSipsIdentity` | Exact expected remote signed identity; distinct from a destination-bank BIC |
| `PapssFacing:SecurityProfile` | Agreed BAH `BizSvc` profile; use the bilateral contract value |
| `PapssFacing:LocalCountry` | Approved two-letter local country code |
| `PapssFacing:SendingCurrencies` | Approved local sending currency list |
| `PapssFacing:ResponderTrust:*` | Approved fingerprint, authority, trust-profile version and EKU where required by the deployed trust contract |
| `PapssFacing:CallbackUrl` | Bank notification receiver HTTPS URL |
| `PapssFacing:CallbackMappingProfile` | Loaded bank-specific PAPSS callback profile |
| `PapssFacing:SpsPolicy:AllowedLocalInstruments` | Optional local SPS restriction; not a replacement for PAPSS directory/capability data |
| `PapssFacing:CrossCurrencyEnabled` | Keep `false` unless separately qualified and approved |

Operational defaults in the current options include a 15-second request timeout, 2,000,000-byte maximum response, 300-second readiness freshness and delivery retries of 10 attempts with 5-second initial / 300-second maximum backoff. These are implementation settings, not PAPSS contractual SLAs. Confirm deployment overrides and agree alert thresholds.

In Guevara, register one active, unambiguous participant signing certificate by exact issuer DN and serial number, with owner, represented BIC, environment, security profile, fingerprint, required EKU and trust authority. Verify it is neither revoked nor suspended.

Do not copy example instruments, currencies, security profiles or certificate values into production without validating them against the actual onboarding contract.

## 5. Differentiating Domestic SIPS and PAPSS transactions

### 5.1 Outbound routing: use the rail explicitly

| Property | Domestic SIPS | PAPSS |
|---|---|---|
| Request selection | Omit `rail` for compatibility, or send `"rail":"SIPS"` | Send `"rail":"PAPSS"` |
| Destination service | Existing SmartVista/IPS route | `PapssFacing:IsoIngressUrl` |
| Signing profile | `IpsVendorLegacy` | `WpSipsPapss` |
| Verify/payment/status/return | Existing Gateway endpoints | Same Gateway endpoints with PAPSS selected |
| Readiness/discovery/FX | Not domestic operations | Require `rail:"PAPSS"` |
| Completion model | Existing domestic contract | Technical admission followed by stored business outcomes and callbacks |

Base path: `https://<SIPS_CONNECT_HOST>/api/v1/Gateway`.

```json
{"rail":"SIPS","txId":"<DOMESTIC-TX-ID>","amount":100.00,"currency":"<CURRENCY>"}
```

```json
{"rail":"PAPSS","txId":"<PAPSS-TX-ID>","amount":100.00,"currency":"<CURRENCY>"}
```

These fragments illustrate routing only; they are not complete payment payloads.

Do not infer the rail from currency, BIC country, account format, local instrument or identifier prefix. A PAPSS transaction can use the same currency on both sides. Unknown rail values must fail; a selected PAPSS transaction must never silently fall back to domestic routing. The current README also describes empty rail as domestic; new integrations should omit it or use an explicit supported value.

### 5.2 Inbound routing and callbacks

Both rails can enter through the signed ISO inbound endpoint. PAPSS handling is established by verified message identity/profile, recipient BIC and the PAPSS processing path; an untrusted JSON label or HTTP header is not proof of origin.

Use the PAPSS callback mapping profile to distinguish the bank's inbound integration. A dedicated PAPSS notification URL is recommended where practical. Critically, inbound PAPSS payment acceptance calls the configured `ISO20022:Transfer` endpoint and bypasses the general PAPSS notification `CallbackUrl` override. The bank must therefore distinguish that transfer flow using its agreed mapping/integration context too.

Current payment status callbacks carry `X-Papss-Operation`, `X-Papss-Source-Message-Id`, `X-Transaction-Id` and `X-Idempotency-Key`; returns add `X-Return-Id`, recalls use `X-Recall-Id`. Header sets vary by callback type. There is no universal `X-Rail` header guaranteed by this contract, and absence of `X-Papss-Operation` does not prove a callback is domestic. Authenticate the sender and use the agreed endpoint/profile and correlated operation record.

### 5.3 Bank records, reporting and accounting

Recommended bank-side transaction fields, to implement or verify before launch:

| Field | Purpose |
|---|---|
| `rail` | Persist `SIPS` or `PAPSS` at initiation or authenticated inbound classification |
| Environment and local bank identity | Separate UAT/production and participant records |
| Direction and operation | Separate inbound/outbound payments, returns, verification and recalls |
| `txId`, `endToEndId`, `requestMessageId` | Preserve bank, gateway and network correlation |
| `returnId`, `recallId`, original references | Link subsequent operations without confusing them with a new payment |
| Amounts/currencies, destination BIC, corridor | Support ledger, FX and reconciliation controls |
| Admission, network outcome, bank delivery and ledger status | Keep transport, payment and accounting states distinct |
| Source event/message ID and processing timestamp | Audit and duplicate suppression |

Namespace bank-side lookup and deduplication keys by environment, bank and rail; include operation/direction where needed. This is a bank integration recommendation, not a claim that every existing SIPS database/API exposes a `rail` column or rail filter. Validate screens and reports against the deployed release. PAPSS operation lookup endpoints described below are PAPSS-specific; they are not unified domestic transaction search endpoints.

Display a visible rail label on payment forms, details, approvals, receipts, exports and support searches. Separate rail totals in reconciliation. Keep the selected rail on all follow-up actions; derive it from the original stored payment rather than a fresh user default.

## 6. Bank integration duties and transaction lifecycle

### 6.1 Outbound payment

1. Authenticate to the Gateway and choose PAPSS explicitly.
2. Call `POST /Readiness` and `POST /Discovery` with `rail:"PAPSS"`. Resolve the destination by an exact, unique BIC and confirm fresh, active, online capabilities.
3. Select a supported receiver currency and local instrument. Sender BIC/country and permitted sending currencies come from deployment configuration. Contradictory supplied sender values fail closed.
4. Verify the beneficiary with `POST /Verify`; wait for the asynchronous business result, not just admission. Apply the bank's match/no-match policy.
5. Where applicable, obtain FX information using `POST /FX`; present agreed fees and amounts. FX enquiry is indicative, not a guaranteed executable quote.
6. Persist the rail, immutable transaction references and bank ledger intent before calling `POST /Payment`. Supply unique `txId` and `localId` (EndToEndId), plus all required payer, payee, account, amount, currency and routing fields.
7. Store the returned `requestMessageId`. A response such as `durablyAdmitted:true` is technical admission, not final settlement.
8. Process authenticated status callbacks idempotently, or reconcile through operation lookup. Update the ledger and customer display only according to the approved state mapping.

For full payloads and account-type conventions, use `PAPSS_BANK_INTEGRATION_GUIDE.md` and the bank's deployed JsonAdapter mapping. Do not assume an example account type or arbitrary reason code is accepted by a destination.

### 6.2 State interpretation

| Signal | Meaning / operational treatment |
|---|---|
| `gatewayState: ADMITTED` | Request admitted; business result may still be pending |
| `SUBMISSION_UNKNOWN` / `UNKNOWN` | Submission or outcome uncertain; reconcile using original references |
| Payment `PDNG` / `PENDING` | Pending; no final payment outcome |
| Payment `ACCP` or `ACSP` / `ACCEPTED` | Accepted or progressing; not final payment settlement |
| Payment `ACSC` / `SETTLED` | Final settled payment outcome in this integration |
| Payment `RJCT` / `REJECTED` | Final rejection; apply approved release/reversal process after checking ledger state |
| Payment `RETURNED` | A linked return was recorded as settled; reconcile the return and original postings |
| `bankDeliveryState: FAILED` | Notification delivery failed; does not establish payment failure |
| `statusConflict:true` | A different final status arrived after a final outcome; first final state retained, operator investigation required |

Interpret statuses by operation. Outbound returns use `PapssFacing:Returns:SettledStatuses`, currently defaulting to `ACCP,ACSC`; therefore a return's `ACCP` can settle the return even though a payment's `ACCP` is not final. A verification marked `COMPLETED` can still be a no-match. Always inspect operation type and business result.

### 6.3 Inbound bank acceptance and notifications

- Inbound PAPSS verification uses `CB_VerificationRequest` / `CB_VerificationResponse` to query the core bank. Return the actual verification result and reason. Core-bank failure does not generate an invented negative result.
- Inbound PAPSS payments always call `ISO20022:Transfer` using the PAPSS profile's `CB_PaymentRequest` / `CB_PaymentResponse`, even when domestic core-bank listing is disabled.
- A successful transfer response containing `ACCP`, `ACSC` or `SUCC` becomes a stored initial `ACCP` decision. Explicit rejection becomes `RJCT` with the bank's reason. Empty, unrecognized, unsuccessful or timed-out responses require reconciliation; no bank decision is submitted automatically.
- SIPS Connect signs and separately publishes the persisted acceptance/rejection decision. An HTTP acknowledgement alone is not the signed business decision, and the initial bank acceptance is separate from subsequent PAPSS settlement.
- Redelivery reuses the persisted decision rather than repeating the transfer call. The bank must still make its own posting idempotent, including after timeout/crash ambiguity.
- Notifications use at-least-once delivery. Persist the event durably and prevent duplicate effects before acknowledging success. Acknowledge within the agreed service target.
- Load profile mappings for verification results, payment requests/responses, completion notifications, return requests and recall results/inbound recall workflows where enabled.

## 7. Activation sequence and go/no-go gates

| Step | Action | Required evidence before proceeding |
|---|---|---|
| 1 | Record approved bank, scope, versions, contacts and activation window | Completed activation record and change approval |
| 2 | Deploy with PAPSS disabled; apply migrations and mappings | Startup succeeds; backup/recovery and database access checked |
| 3 | Run domestic Verify, Payment, Status and Return regression | Omitted and explicit SIPS rail retain existing behavior |
| 4 | Validate credentials, trust registration, networking and bank receivers | Correct identities succeed; invalid identities fail closed |
| 5 | Enable PAPSS in UAT using the normal configuration rollout | All intended nodes have the same effective configuration |
| 6 | Run the UAT matrix below | Expected results, final statuses and ledger reconciliation evidenced |
| 7 | Close technical defects and confirm production technical enrollment | SPS technical, security and operations sign-off; bank business launch approval remains separate |
| 8 | Deploy approved production configuration and enable PAPSS | Startup/health, signed readiness/discovery and callbacks verified |
| 9 | Submit agreed low-value pilot payments with approved counterparties | Outbound and inbound posting, final status and reconciliation match |
| 10 | Expand to approved volumes and hand over | Monitoring owned, support available and pilot exceptions closed |

Use `GET /Health` to inspect component health, not just HTTP reachability. Healthy components do not prove the destination corridor is eligible or a payment will settle; perform signed readiness/discovery and end-to-end checks too.

Technical go/no-go: SPS stops or contains new PAPSS initiation for unresolved signing/correlation failures, wrong participant identity, duplicate message effects or unproven inbound acceptance. The bank separately controls initiation for ledger discrepancies, funding or liquidity issues under its own procedures. For business deadlines and acceptable pending ages, use the signed service agreement and bank policy; do not substitute an implementation timeout.

## 8. Minimum UAT acceptance matrix

| Test | Expected evidence |
|---|---|
| PAPSS disabled | PAPSS rejected; domestic flows work; no silent rail fallback |
| Rail handling | Omitted/SIPS route domestic; PAPSS routes correctly; unknown rail rejected |
| Readiness/discovery/FX rail restriction | Missing/SIPS rail rejected for PAPSS-only endpoints |
| Authentication / authorization | Valid Gateway credentials work; invalid credentials/role fail |
| Identity and trust | Wrong recipient BIC, invalid signature, wrong remote identity or expired/untrusted certificate rejected |
| Directory/capability failures | Offline, stale, ambiguous, unsupported currency/instrument observations fail closed |
| Beneficiary verification | Match/no-match business result delivered and correlated after admission |
| Outbound payment | Admission, advancing statuses, final outcome and single ledger effect evidenced |
| Inbound payment | Actual core-bank transfer call and separately published signed decision evidenced |
| Inbound timeout/rejection | No false acceptance; bank reason preserved; uncertain posting investigated |
| Exact retry | Same `txId` and same content do not create a second payment |
| Conflicting duplicate | Same `txId` with changed content returns `DUPLICATE_CONFLICT` |
| Ambiguous delivery | Lost response recovered through lookup and identical-reference retry |
| Duplicate/out-of-order callbacks | No double posting or backwards final state; conflicts visible |
| Callback outage and recovery | Payment outcome persists independently; retry/failed delivery and bank reconciliation proven |
| Status enquiry | Original references preserved; final payment answered from store; non-final enquiry correlated |
| Return | Unique return ID, original linkage, configured return settlement interpretation and ledger match |
| Recall, if enabled | Outbound/inbound workflow, rejection/return outcome and no false claim of recovered funds |
| Restart / multiple instances | Durable operations/outboxes survive restart; no duplicate bank effects |
| Daily reconciliation | Bank ledger, SIPS operation/events and PAPSS/sponsor reports explain all differences |
| Cross-currency, if enabled | Both feature switches, live payment quote, fees, instrument and amount semantics qualified |

Record build/mapping versions, sanitized requests, identifiers, UTC timestamps, callbacks, expected/actual outcomes and ledger/report evidence. Use new UAT transactions for inbound acceptance tests; historical transactions are not automatically transferred again.

## 9. Lookup, timeout and incident procedures

### 9.1 Supported lookup routes

All paths below are under `/api/v1/Gateway` and require the appropriate role:

| Route | Use |
|---|---|
| `GET /Operations/{requestMessageId}` | PAPSS operation by gateway ID or inbound PAPSS source message ID |
| `GET /Payment/{txId}` | Payment lookup; outbound wins if an inbound payment shares the TxId |
| `GET /Operations?endToEndId=<id>` | Payment by EndToEndId; outbound/newest precedence applies |
| `GET /Verify/{requestMessageId}` | Outbound verification |
| `GET /Return/{returnId}` | Return lookup |
| `GET /Recall/{recallId}` | Recall and linked outcomes |
| `GET /Operations/Unresolved?limit=50` | Uncorrelated/conflicting/unknown events; Gateway or Recon role |

Check direction, counterparty, amount, currency and original references; a matching TxId alone is insufficient where precedence rules can select another operation. Unknown IDs return `404 OPERATION_NOT_FOUND`. Optional `waitSeconds` works only up to the configured lookup maximum; long-polling is off by default.

### 9.2 Payment submission times out or returns an ambiguous error

1. Mark the bank workflow as uncertain and preserve the original payload, rail, `txId`, EndToEndId and any returned request ID.
2. Query the operation/payment store and inspect gateway state, payment outcome and status history.
3. For a non-final outbound payment, use `POST /Status` with `rail:"PAPSS"`, original `txId`, `endToEnd` and destination `toBIC` as appropriate. Final payments are answered from storage without a new network enquiry.
4. If submission remains unknown, retry the identical payment with the same `txId` and content under the agreed bounded retry procedure. SIPS Connect reuses stored signed bytes; exact replay is not a second payment.
5. Never create a replacement TxId merely because the response was lost. Never release/debit again based solely on a timeout. Escalate persistent uncertainty with sanitized evidence.

The same principle applies to `returnId`, `recallId` and verification `MsgId`, using the identifiers supported by the deployed mappings. Outbound operations left at `SUBMISSION_UNKNOWN` are not automatically resubmitted by the current workers.

### 9.3 Callback failure

1. Look up the operation. Separate the PAPSS outcome from `bankDeliveryState`.
2. Restore receiver connectivity/authentication and inspect mapping/version compatibility.
3. Confirm whether automatic attempts remain. After retries are exhausted, reconcile from the stored result using the bank's approved recovery procedure; do not assume an undocumented replay endpoint exists.
4. Apply any missing bank effect idempotently and record the recovery evidence. Never resubmit the payment to repair a notification failure.

### 9.4 Uncorrelated or conflicting result

Inspect `/Operations/Unresolved`, `statusConflict`, `statusHistory`, source message IDs and raw evidence references. A different final status does not overwrite the first final state or generate an advancing bank notification. Escalate to SIPS/PAPSS operations before a corrective financial action.

Status provenance distinguishes `NETWORK_REPORTED` from `LOCAL_RECONSTRUCTION`, `IDENTIFIER_TRANSLATION`, `DEFAULT_FILLER` and legacy/unspecified values. An amount reconstructed from the original payment is not independent confirmation that PAPSS reported that amount.

### 9.5 Error triage

| Signal | First response |
|---|---|
| `PAPSS_DISABLED` | Check approved scope and effective deployment switch |
| 401/403 | Fix credentials or role; do not repeatedly submit |
| 400 / `DUPLICATE_CONFLICT` | Inspect payload/rail/reference reuse; do not change IDs to bypass a conflict |
| 422 | Correct the stated business/corridor validation issue after confirming no admission |
| 502 / invalid signed response | Investigate signature, identity, correlation and trust; reconcile financial requests |
| 503 / timeout | Check connectivity/availability; preserve references and resolve uncertain delivery before retry |
| `PAPSS_FX_FEE_NOT_READY` | Cross-currency not enabled/qualified; do not downgrade or change the rail |

## 10. Returns, recalls and cross-currency scope

`POST /Return` initiates a pacs.004 return, normally against a received payment. It uses a new unique `returnId` and the original payment references, amount, currency and required routing data.

`POST /Recall` asks for recovery of a payment the bank sent. By default it requires a settled original and allows a 30-day window from settlement. The configured beneficiary response deadline defaults to 30 days and is record-only. These defaults reflect repository evidence and must be confirmed for the production contract. Only one open recall per payment is supported by the local correlation rules. A recall admission or acceptance is not returned funds; actual return evidence is required. There is no PAPSS recall status enquiry equivalent to the payment enquiry.

For inbound recalls, qualify the deployed decision workflow separately, including `POST /Recall/Inbound/{recallId}/Decision`, permissions, beneficiary authorization, available funds and idempotent return posting. Current documented rejection reasons are `LEGL`, `AGNT`, `CUST`, `ARDT`, `NOAS`, `NOOR`, `AC04`, `AM04`; `FOCR` is a return reason, not a recall rejection reason. Manual closure of a recall does not prove recovery or change the original payment into a returned payment.

Cross-currency is a separate activation gate. The currently documented USD-to-local flow requires both `PapssFacing:CrossCurrencyEnabled=true` and `PAPSS__CROSS_CURRENCY_ENABLED=true` on the Transactions Gateway. It uses `SSA3`, a USD sender amount and a live PAPSS quote obtained by the gateway. `USDP` is USD-to-USD only. FX enquiry may return `FX_NOT_OBSERVED`; it is not a reserved quote. Returns and recalls of cross-currency payments are currently unsupported. Agree an operational exception path and obtain explicit scope approval before enabling this flow.

## 11. Monitoring and daily reconciliation

Monitor and assign owners for:

- PAPSS availability, signature/trust failures, certificate expiry and directory freshness.
- Admission failures, submission-unknown counts and payment age by state/direction.
- Bank notification failures and outbox age, pending/failed signed decisions and verification replies.
- Inbound accepted payments with no network final result, unmatched events and final-status conflicts.
- Recall deadlines and unresolved return outcomes.
- Bank integration duplicate-processing alerts and technical delivery discrepancies. Bank ledger, liquidity and funding monitoring remains bank-owned.

SPS performs technical reconciliation of submitted messages, operation/event/decision records, acknowledgements and bank callback delivery. It supplies correlation evidence for technical investigations.

The participant bank separately performs financial reconciliation across its ledger and integration inbox, SIPS technical evidence, and PAPSS or sponsor transaction/settlement reports. SPS does not perform settlement reconciliation. Match by original references, direction, currency, amount, returns and final status. Reconcile FX and fees separately where applicable. Track each break with owner, age, evidence and resolution.

Retention is unset by default, meaning no automatic purge; decide retention, privacy and capacity requirements explicitly. Do not enable a purge period that removes evidence required for reconciliation, disputes or recalls. Include database and protected message evidence in recovery planning.

Some states need active follow-up: an inbound verification can remain pending after a crash; an accepted inbound payment can remain non-final without a PAPSS completion; and an overdue recall does not automatically resolve. Optional inbound reconciliation enquiries are disabled by default and should be enabled only after the receiving-participant enquiry behavior is confirmed for the environment.

## 12. Containment and rollback

1. Stop new PAPSS initiation at the bank channel/approval layer if necessary, preserving pending original references.
2. Inventory in-flight operations, pending bank notifications and unsigned/unpublished decisions before changing service state.
3. For full feature disable, set `PapssFacing:Enabled=false` using the normal configuration/restart procedure and verify it on all instances. New PAPSS requests should be rejected; domestic omitted/SIPS requests should retain their existing route.
4. Coordinate inbound traffic handling and the in-flight drain/recovery plan with the PAPSS operator. Several PAPSS workers run only while the feature is enabled, so a full disable can halt delivery/reconciliation work. Do not assume disabling safely drains existing transactions or provides inbound PAPSS processing.
5. Continue ledger/network reconciliation through an agreed controlled procedure. Do not route pending PAPSS payments domestically, delete the operation store, restore an old database over live financial records or blindly resubmit transactions.
6. Fix the cause, repeat the relevant UAT/pilot checks and approve reactivation. Check unresolved/outbox backlogs after re-enabling.

## 13. Go-live acceptance record

- [ ] Technical enrollment and approved corridor scope confirmed; bank financial readiness remains outside SPS sign-off.
- [ ] Production identity, certificates, trust registration and credential separation verified.
- [ ] All nodes use the approved release, configuration and mapping version.
- [ ] Domestic regression passed with PAPSS both disabled and enabled.
- [ ] Outbound/inbound PAPSS payments and beneficiary verification passed end to end.
- [ ] Initial acceptance, network finality, bank delivery and ledger states remain distinct.
- [ ] Duplicate, timeout, restart and callback-failure recovery passed without double posting.
- [ ] Return/recall scope proven; cross-currency limitations accepted where applicable.
- [ ] SPS pilot message and callback reconciliation completed with no unexplained technical breaks; bank owns ledger and settlement checks.
- [ ] Monitoring, certificate rotation, support ownership and rollback procedure accepted.
- [ ] Named bank integration, SPS technical operations, security and release approvers recorded; bank business approvals tracked separately.

## 14. Sources and interpretation

Implementation sources: `README.md` (PAPSS Integration), `docs/PAPSS_BANK_INTEGRATION_GUIDE.md`, `docs/PAPSS_PARTICIPANT_ADAPTER.md`, `docs/PAPSS_OPERATION_STORE_CONFIG.md`, `SIPS.Connect/Config/PapssFacingOptions.cs`, and the current PAPSS event delivery, callback guard and worker implementations.

Some older planning notes in `docs/papss-delivery.md` state that recall is unsupported or describe per-participant enablement. The current code and integration guide support recalls and use a deployment-wide PAPSS switch with one configured bank identity; this runbook follows those current sources. Re-validate against the actual deployed release before execution.

External participation/funding context: [PAPSS Get connected](https://papss.com/get-connected/) and [PAPSS How it works](https://papss.com/how-it-works/), checked 9 November 2026. Environment-specific agreements govern approved funding arrangements, deadlines, fees and service scope.
