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

For the core-bank call that answers an inbound acmt.023, SIPS Connect uses the existing `Core:CoreBankTimeoutSeconds` setting (default `3`), so behaviour there is unchanged. The WP-SIPS request timeout is still `PapssFacing:RequestTimeoutSeconds`.

Validation at start-up: positive values where a number is set; the deadline value and clock set together; `MaxBackoffSeconds >= InitialBackoffSeconds`; `ClaimLeaseSeconds >= 10`; `Lookup:MaxWaitSeconds` from 0 to 300. An empty environment variable means "unset".

## Database objects

The migration is `20260924234525_AddPapssOperationStore`. The tables are separate from `isomessages`. Column names follow the repository's lower-case naming convention.

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
- `papss_operation_events`: an append-only log of received messages.
  - Each event is unique per `(eventtype, sourcemessageid)`, so a redelivered callback is stored once.
  - Each event carries its bank push outbox columns: `pushstate`, `pushattempts`, `pushnextattemptat`, `pushlasterror`, `pushdeliveredat`.
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

The lookup API adds one summary `status`:
- `PENDING`
- `COMPLETED`
- `REJECTED`
- `FAILED`: inbound only. The core bank did not answer, or the reply was held.
- `UNKNOWN`: the submission outcome is ambiguous, or the reply retries ran out.
- `EXPIRED`: only when `VerificationResultExpirySeconds` is set.

## Workers

All state lives in the database. After a restart, the workers carry on with whatever is pending. More than one instance can run safely: an instance claims an item with a compare-and-set on `(state, attempts)`, and the claim is held for the lease period. Delivery is at-least-once. The bank de-duplicates on `X-Idempotency-Key` (the verification id). The gateway de-duplicates the byte-identical reply and answers `EXACT_REPLAY`.

- `PapssBankPushWorker`: delivers events whose `pushstate` is `PENDING` to the bank.
  - It uses the `CB_VerificationResult` mapping of the configured `CallbackMappingProfile` and sends to `CallbackUrl`.
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

## Known limitations

These are Phase 1 limitations.

- An inbound enquiry can stay at `bankdeliverystate=PENDING` indefinitely. This happens when the process crashes after the enquiry was stored and before the core bank answered. A redelivery from PAPSS deliberately does not call the core bank again. The row is visible through the lookup API.
- An outbound verification left at `SUBMISSION_UNKNOWN` is not re-submitted automatically. When the bank calls `/Verify` again with the same `MsgId`, SIPS Connect re-submits the stored signed bytes. A result that arrives from PAPSS also settles it.
- Only verification is stored. Payment, status enquiry and return are reserved operation types.

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
