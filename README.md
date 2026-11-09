# SPS Connect (previously SIPS Connect)

**SPS Connect** (previously **SIPS Connect**) is the official product name of the participant-side integration gateway owned by **SOMALI PAYMENT SWITCH (SPS) LTD**. It connects participant core banking systems to both **domestic transactions through SIPS/SVIP (SmartVista/IPS)** and **cross-border transactions through the Pan-African Payment and Settlement System (PAPSS)**.

Banks integrate through authenticated JSON APIs. SPS Connect handles rail selection, ISO 20022 message orchestration, JSON adaptation, digital signing and verification, transaction persistence, correlation, idempotency, callbacks and technical recovery. It also provides SomQR services, dashboards, health checks, metrics and operational configuration.

SPS serves only as the technical connector participant. Settlement, pre-funding, liquidity management and financial settlement reconciliation remain the responsibility of participant banks and their PAPSS or sponsor arrangements. SPS Connect records and communicates network-reported payment outcomes for technical processing and traceability; it does not perform settlement.

The repository, solution, project names, namespaces, configuration keys and deployment identifiers retain their existing `SIPS` / `SIPS.Connect` names. These technical identifiers do not change the official product name, **SPS Connect**.

## Domestic and Cross-Border Transactions

| Transaction rail | Service | Selection |
|---|---|---|
| Domestic SIPS | Domestic transactions through the existing SIPS/SVIP SmartVista/IPS integration | Omit `rail` or send `"rail": "SIPS"` |
| PAPSS | Cross-border transactions through the configured PAPSS WP-SIPS service | Send `"rail": "PAPSS"`; requires PAPSS activation |

The existing verification, payment, status and return endpoints support rail selection. PAPSS adds readiness, participant discovery, FX enquiries, durable operation lookups and recall workflows. Available currencies, instruments and operations depend on the deployed configuration and current PAPSS capabilities.

The rail is an explicit routing choice, not something inferred from currency or destination country. Preserve it with the original transaction references for callbacks, status enquiries, returns and reporting. PAPSS is disabled by default; an explicitly selected PAPSS request must not silently fall back to domestic routing.

---

## Ownership, Authorship & Licensing

This repository is owned by **SOMALI PAYMENT SWITCH (SPS) LTD**.

**Technical leadership and implementation:** SPS Connect was designed, built, deployed, and productionized by Abdulshakur Ahmed Aided ([@hanadderia](https://github.com/hanadderia)) within the Somali Payment Switch institutional context.

This repository is released under the **Apache License 2.0**. See [LICENSE](LICENSE) and [NOTICE](NOTICE).

Release governance is documented through:

- [NOTICE](NOTICE) - institutional copyright, attribution, and trademark notice.
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) - third-party and product-reference notices.
- [SECURITY.md](SECURITY.md) - vulnerability reporting and sensitive-material policy.
- [CONTRIBUTING.md](CONTRIBUTING.md) - contribution rules and high-risk review areas.
- [CODEOWNERS](CODEOWNERS) - institutional maintainers.

---

## Architecture Overview

SPS Connect is a modular .NET platform composed of:

- **SIPS.Connect** - API host, controllers, configuration endpoints, health checks, metrics, and runtime wiring.
- **SIPS.Core** - payment orchestration, inbound/outbound handlers, callback orchestration, status mapping, recovery workers, and shared services.
- **SIPS.ISO20022** - ISO 20022 schemas, builders, parsers, DTOs, and message transformation helpers.
- **SIPS.XMLDsig.Xades** - XMLDSig/XAdES signing, signature verification, certificate handling, and PKI-off support.
- **SIPS.PostgreSQL** - persistence, migrations, ISO message store, transaction records, status history, and de-duplication constraints.
- **SIPS.Adapter** - configurable JSON mapping between participant/CoreBank payloads and internal DTOs.
- **SIPS.Emv** - SomQR/EMV QR payload generation, parsing, TLV encoding, and CRC validation.

The repository enforces explicit project references through [Directory.Build.props](Directory.Build.props), keeping package dependencies auditable and intentional.

---

## Core Capabilities

- Domestic SIPS/SVIP and cross-border PAPSS transaction routing through a common bank-facing JSON API.
- PAPSS readiness, participant discovery, FX enquiries, operation tracking, callbacks and recall workflows.
- Outbound payee verification, payment initiation, payment status inquiry, and return initiation.
- Inbound switch message processing for verification, credit transfer, status, return, and completion flows.
- ISO 20022 XML generation and parsing for payment, verification, status, return, and administrative messages.
- Configurable JSON adapter mappings for CoreBank integration.
- XAdES/XMLDSig signing and signature verification.
- Public-key/certificate retrieval and caching.
- API-key and bearer-token authentication support.
- PostgreSQL-backed transaction, ISO message, response, and status persistence.
- Insert-first de-duplication, deterministic response replay, and duplicate-in-process handling.
- Multi-return prevention and return de-duplication.
- Store-and-forward and timeout recovery workers.
- CoreBank callback orchestration with bounded timeout handling.
- SomQR merchant and person QR generation/parsing.
- Prometheus metrics, structured logs, health checks, dashboards, and operational query APIs.

---

## Financial Safety Controls

SPS Connect includes payment-system controls designed to protect transaction correctness and operational traceability:

- **Insert-first de-duplication:** inbound messages are persisted before processing so concurrent duplicates resolve deterministically.
- **Message and transaction idempotency:** duplicate `MsgId` and `TxId` values are detected per message type.
- **Deterministic replay:** completed duplicate requests return the stored response instead of reprocessing.
- **Return safety:** return requests are gated against original transactions to reduce double-reversal risk.
- **Ledger-as-truth model:** persisted participant-side state provides the authoritative switch-facing record after confirmation and acknowledgement.
- **Status recovery:** indeterminate transactions can be marked for status inquiry and retried through scheduled recovery workers.
- **Audit persistence:** raw messages, responses, statuses, identifiers, amounts, parties, UETR, return IDs, and reasons are retained for investigation and reconciliation.

---

## Configuration

Runtime configuration is loaded from:

- `SIPS.Connect/appsettings.json`
- `SIPS.Connect/appsettings.Development.json`
- `jsonAdapter.json`
- environment variables

Secrets are intentionally not committed. Deployment-specific credentials, certificate passwords, API keys, callback secrets, private-key passphrases, and database passwords must be supplied through environment variables, secret stores, or secured deployment configuration.

Important configuration areas:

- `ConnectionStrings:db`
- `Core`
- `Xades`
- `PapssFacing`
- `ISO20022`
- `Emv`
- `Keycloak`
- `CorsPolicies`
- `ApiKeys`

## PAPSS Integration

SPS Connect can route the existing participant operations to either the domestic IPS or PAPSS. Each deployment represents one participant bank. The bank identity is read from `Xades:BIC`; it is not selected from an HTTP identity claim or request payload.

| Request rail | Destination | XAdES profile |
|---|---|---|
| omitted, empty, or `SIPS` | Existing SmartVista/IPS endpoint | `IpsVendorLegacy` |
| `PAPSS` | `PapssFacing:IsoIngressUrl` | `WpSipsPapss` |

PAPSS is disabled by default. A disabled PAPSS configuration does not create a PAPSS network dependency and does not affect existing SmartVista traffic.

### Configuration

Use deployment-specific values and keep private keys and passphrases in the deployment secret store. The following example contains placeholders only:

```json
{
  "Xades": {
    "BIC": "<LOCAL-BANK-BIC>",
    "CertificatePath": "/certs/<SIGNING-CERTIFICATE>.pem",
    "PrivateKeyPath": "/certs/<SIGNING-PRIVATE-KEY>.key",
    "PrivateKeyPassphrase": "<SECRET-INJECTION>",
    "ChainPath": "/certs/<WP-SIPS-TRUST-CHAIN>.pem",
    "Algorithms": ["SHA256withRSA"],
    "DefaultSignatureMethod": "SHA256withRSA",
    "VerificationWindowMinutes": 100,
    "WithoutPKI": false,
    "BaseDN": "<EXPECTED-ISSUING-CA-DN>"
  },
  "PapssFacing": {
    "Enabled": false,
    "IsoIngressUrl": "https://<PAPSS-SERVICE-HOST>/sips/messages",
    "AllowedHosts": ["<PAPSS-SERVICE-HOST>"],
    "Environment": "UAT",
    "RemoteWpSipsIdentity": "<PAPSS-WP-SIPS-IDENTITY>",
    "SecurityProfile": "<WP-SIPS-BUSINESS-SERVICE>",
    "RequestTimeoutSeconds": 30,
    "MaximumResponseBytes": 2000000,
    "ReadinessStaleSeconds": 300,
    "SpsPolicy": { "AllowedLocalInstruments": [] },
    "LocalCountry": "<ISO-3166-ALPHA-2>",
    "SendingCurrencies": ["<ISO-4217>"],
    "CallbackMappingProfile": "papss-callback-v1",
    "CallbackUrl": "https://<BANK-INTERNAL-HOST>/<CALLBACK-PATH>"
  }
}
```

Configuration reference:

| Configuration | Meaning and use | Supplied by / example |
|---|---|---|
| `Xades:BIC` | The identity of the single participant represented by this SPS Connect deployment. It is protected by the signature and used as the outbound Business Application Header (BAH) `From` value and the expected inbound BAH `To` value. | The participant bank and its certificate-registration process; for example `BANKSOSIXXX`. |
| `Xades:CertificatePath`, `PrivateKeyPath`, `ChainPath` | The participant signing certificate, its private key, and the trust chain used to sign outbound WP-SIPS messages and verify trusted signed traffic. | Deployment/PKI administrators. Keep the private key and its passphrase in the deployment secret store. |
| `PapssFacing:Enabled` | Deployment-wide switch for the PAPSS rail. When `false`, PAPSS requests are rejected and existing IPS routing remains unaffected. | SPS Connect operator; enable only after trust, routing, and mappings have been validated. |
| `PapssFacing:IsoIngressUrl` | The PAPSS service endpoint to which every signed WP-SIPS request is posted. | PAPSS service operator; for example `https://papss.example/sips/messages`. |
| `PapssFacing:AllowedHosts` | Explicit outbound-host allowlist for the PAPSS URL. The host in `IsoIngressUrl` must be listed here. | SPS Connect security/operator team; for example `papss.example`. |
| `PapssFacing:Environment` | Labels the configured PAPSS operating environment, such as UAT or production. It is required when PAPSS is enabled, but is not a participant identity or payment-routing field. | PAPSS service/operator deployment agreement; use the exact bilateral environment value. |
| `PapssFacing:RemoteWpSipsIdentity` | The expected signed identity of the remote PAPSS WP-SIPS service. SPS Connect writes it as outbound BAH `To` and requires it as inbound/response BAH `From`; it is not a receiver-bank BIC or a selectable participant. | PAPSS service operator; for example `PAPSS`. It must match PAPSS's certificate/identity contract exactly. |
| `PapssFacing:ResponderTrust:CertificateSha256` | SHA-256 fingerprint pin for the PAPSS WP-SIPS response-signing certificate. It safely supplements legacy Guevara responses that do not yet carry WP-SIPS provenance metadata. | Derive from the approved PAPSS public certificate; 64 hexadecimal characters. Rotate deliberately with the certificate. |
| `PapssFacing:ResponderTrust:Authority` | Authority that registered the pinned PAPSS response signer. | SPS trust-domain configuration; for example `SPS`. |
| `PapssFacing:ResponderTrust:TrustProfileVersion` | Ownership/security profile under which the pinned signer is accepted. | Use the deployed XAdES trust profile, for example `SPS.XADES.BES.001@1.0.0`. |
| `PapssFacing:ResponderTrust:RequiredExtendedKeyUsageOid` | EKU that the pinned PAPSS signing certificate must contain. | For the UAT certificate used for client authentication and signing, `1.3.6.1.5.5.7.3.2`. |
| `PapssFacing:SecurityProfile` | The agreed WP-SIPS business-service identifier carried in BAH `BizSvc`. It tells both systems which message/security processing contract applies; it does not identify either party. | PAPSS/WP-SIPS integration contract; for example `SPS.PAPSS.FINANCIAL.001`. Use the exact value PAPSS supports. |
| `PapssFacing:RequestTimeoutSeconds` | Maximum time allowed for a PAPSS request. | SPS Connect operator, within operational limits. |
| `PapssFacing:MaximumResponseBytes` | Maximum PAPSS response size accepted before processing is stopped. | SPS Connect security/operator team. |
| `PapssFacing:ReadinessStaleSeconds` | Freshness threshold for signed PAPSS Discovery and Readiness observations used to validate payments; it also governs PAPSS readiness health freshness. | Agreed operational policy. |
| `PapssFacing:SpsPolicy:AllowedLocalInstruments` | Optional local restriction on PAPSS instruments. An empty array adds no local restriction; PAPSS remains authoritative through signed Discovery/Readiness data. | SPS Connect operator, only when SPS deliberately imposes a narrower policy. |
| `PapssFacing:LocalCountry` | Country of this deployment's participant, used as the sender country. It is not the receiver country. | Participant deployment data; ISO 3166-1 alpha-2, for example `SO`. |
| `PapssFacing:SendingCurrencies` | Currencies this participant deployment may send on the PAPSS rail. | Participant deployment data; ISO 4217, for example `SOS`. |
| `PapssFacing:CallbackMappingProfile` | Prefix selecting the JsonAdapter mappings used when translating verified PAPSS callbacks for the participant. | SPS Connect integration team; for example `papss-callback-v1`. |
| `PapssFacing:CallbackUrl` | Internal participant endpoint that receives verified and mapped PAPSS callbacks. | Participant/integration team; use an HTTPS endpoint controlled by the participant. |

`RemoteWpSipsIdentity` and `SecurityProfile` are deliberately separate: the first answers **“who is the remote signed party?”**, while the second answers **“which WP-SIPS business service governs this message?”**. Neither value describes the destination bank of an individual payment.

One SPS Connect deployment represents one local participant. `Xades:BIC` supplies its protected sender identity; `PapssFacing` only adds the local country, sending currencies, PAPSS route, and callback transport. Authentication remains mandatory at the API boundary, but there is no participant registry or per-operation policy inside SPS Connect. `SpsPolicy:AllowedLocalInstruments` is an optional SPS restriction; PAPSS-supported instruments are always learned from signed discovery/readiness data.

For the supplied Docker Compose manifests, configure those values with:

```dotenv
PAPSS_LOCAL_COUNTRY=<ISO-3166-ALPHA-2>
PAPSS_SENDING_CURRENCY=<ISO-4217>
PAPSS_SPS_POLICY_LOCAL_INSTRUMENT=
```

`PAPSS_SPS_POLICY_LOCAL_INSTRUMENT` should remain empty unless SPS deliberately restricts the PAPSS instruments reported by Discovery/Readiness. Do not configure `PAPSS_RECEIVER_COUNTRY`, `PAPSS_RECEIVER_CURRENCY`, `PAPSS_DESTINATION_BIC`, or an `AllowedCorridors` section; those values are transaction-selected or PAPSS-owned.

Payment destination BIC and, where needed, receiver currency are transaction selections. SPS Connect performs signed, correlated Discovery and Readiness lookups for every PAPSS payment, derives receiver country, and validates current status, online eligibility, currencies and payment schemas. No per-destination deployment entry is used.

All PAPSS operations use one service ingress:

```text
POST https://<PAPSS-SERVICE-HOST>/sips/messages
Content-Type: application/xml
signed ISO 20022 using WpSipsPapss
```

The existing `/Verify`, `/Payment`, `/Status`, and `/Return` JSON mappings accept an optional `rail` field. Omitting it preserves domestic IPS behavior. `/Readiness`, `/Discovery`, and `/FX` are PAPSS-only and require `rail=PAPSS`. `GET /api/v1/Gateway/PAPSS/Positions?limit=20` returns only the authenticated bank's PAPSS reconciliation position history and never accepts a participant identity from the caller.

### Safe enablement

1. Deploy SPS Connect with `PapssFacing:Enabled=false`.
2. Verify normal SmartVista/IPS traffic and health.
3. Install the local WP-SIPS signing certificate/private key and the PAPSS verification trust chain.
4. Configure `/sips/messages`, its allowed host, the expected PAPSS identity, security profile, local country, and sending currency. Add an SPS instrument restriction only if deliberately required.
5. Load the PAPSS request and callback JSON adapter mappings.
6. Set `PapssFacing:Enabled=true` and restart SPS Connect so startup validation runs.
7. Verify readiness and discovery, then test verification before enabling financial UAT flows.

Complete the [PAPSS Activation Runbook](docs/PAPSS_ACTIVATION_RUNBOOK.md) for technical activation, domestic regression, bank acceptance, callback recovery and operational handover. Bank settlement and funding prerequisites are managed separately by the bank and PAPSS or its sponsor.

### PAPSS payment decisions and admission

An inbound PAPSS `pacs.008.001.10` is verified with `WpSipsPapss`, delivered to the configured bank transfer endpoint using the PAPSS mapping profile, and converted into one durable `ACCP` or `RJCT` decision when the bank supplies a recognized acceptance or explicit rejection. SPS Connect persists the exact signed `pacs.002.001.12` before posting it separately to `POST /sips/messages`. HTTP success alone acknowledges delivery only; the mapped bank response supplies the acceptance or rejection that SPS Connect publishes as a separate signed business decision. An empty, unrecognized or timed-out bank response requires reconciliation and does not automatically produce acceptance. Ambiguous handoffs are retried from the stored signed bytes, so neither a callback retry nor a timeout creates a second decision.

`RECEIVED_AND_DURABLY_ADMITTED` and `EXACT_REPLAY` are technical admission outcomes. They do not mean beneficiary verification, payment acceptance, settlement, return completion, or authoritative status. Stable admission failures are exposed as `DUPLICATE_CONFLICT`, `AUTHORIZATION_REJECTED`, `REJECTED_BEFORE_EXTERNAL_EFFECT`, or `UNSUPPORTED_PROFILE`.

### Guevara certificate registration

For every SPS Connect deployment, Guevara must contain exactly one unambiguous active certificate record matching the signing certificate's issuer DN and serial number. The record must contain the certificate owner, exact represented participant BIC, environment, WP-SIPS security profile, required EKU, SHA-256 fingerprint, SPS authority, and a non-revoked/non-suspended status. The participant keeps its certificate and private key locally; no private key is sent to PAPSS Adapter or Guevara. PAPSS Adapter mTLS and PAPSS XML-signing certificates are not SPS Connect settings.

Use `Sps.Sips.XmlSecurity.Xades` version `1.0.3` or later. Version `1.0.3` keeps the PAPSS identified-envelope signature while removing the PAPSS-only `FPEnvelope/@Id` from legacy IPS messages.

Detailed integration and mapping guidance:

- [PAPSS Bank Integration Guide](docs/PAPSS_BANK_INTEGRATION_GUIDE.md)
- [PAPSS Participant Adapter](docs/PAPSS_PARTICIPANT_ADAPTER.md)
- [XAdES Profile-Separation Evidence](docs/XADES_PROFILE_SEPARATION_EVIDENCE.md)

---

## Container Images and GitHub Actions

SPS Connect images are built on GitHub Actions and published to GitHub Container Registry as `ghcr.io/sps-sips/sps-connect`, supporting `linux/amd64` and `linux/arm64`.

- Pull requests to `main` validate the multi-platform Docker build without publishing.
- Pushes to `main` publish `edge` and a source-commit tag.
- Version tags such as `v2.0.1`, or manual release runs from `main`, publish the numbered version and `latest`.
- The initial GHCR release uses version **2.0.0**, rebuilt from current source. Ordinary main builds do not overwrite numbered releases.

Banks should use the [Bank Container Image Guide](docs/BANK_CONTAINER_INSTALLATION.md) for download and deployment instructions.

See [Container Publishing](docs/CONTAINER_PUBLISHING.md) for the first `2.0.0` publication, versioning, package permissions and migration from Docker Hub. The workflow uses GitHub's built-in token and does not require Docker Hub credentials. Publishing an image does not deploy it.

---

## Local Two-Node Simulation

The two-node Docker topology simulates two independent participant gateways with separate PostgreSQL databases. This avoids local loopback collisions caused by strict `(MessageType, TxId)` de-duplication.

Start Node A:

```bash
cp .env.node-a.example .env.node-a
docker compose -f docker-compose.node-a.yml --env-file .env.node-a up -d --build
```

Start Node B:

```bash
cp .env.node-b.example .env.node-b
docker compose -f docker-compose.node-b.yml --env-file .env.node-b up -d --build
```

Default local endpoints:

| Node | API | Health | PostgreSQL |
|---|---|---|---|
| Node A | `http://localhost:8080/api/v1` | `http://localhost:8080/health` | `localhost:5432` |
| Node B | `http://localhost:9080/api/v1` | `http://localhost:9080/health` | `localhost:6432` |

Default peer routing:

| Flow | Source | Target |
|---|---|---|
| A to B | Node A | `http://host.docker.internal:9080/api/v1/incoming` |
| B to A | Node B | `http://host.docker.internal:8080/api/v1/incoming` |

Single-node mode is intended for external switch connectivity only. Local self-routing is not supported for transaction or verification flows because it can collide with transaction-level de-duplication.

---

## JSON Adapter Updates

The JSON adapter files define the mapping between CoreBank/local JSON payloads and the internal SPS Connect DTOs:

- `jsonAdapter.json`
- `jsonAdapter.node-a.json`
- `jsonAdapter.node-b.json`

For Docker-based local simulation, adapter files are mounted into the containers. After changing adapter mappings, restart the relevant SPS Connect container:

```bash
docker compose -f docker-compose.node-a.yml restart sips-connect-a
docker compose -f docker-compose.node-b.yml restart sips-connect-b
```

---

## API Surface

Primary gateway endpoints (Verify, Payment, Status and Return accept rail selection):

- `POST /api/v1/gateway/Verify`
- `POST /api/v1/gateway/Payment`
- `POST /api/v1/gateway/Status`
- `POST /api/v1/gateway/Return`
- `POST /api/v1/gateway/Retry/{id}`
- `POST /api/v1/incoming`

PAPSS-specific endpoints (subject to authentication, configuration and operation eligibility):

- `POST /api/v1/Gateway/Readiness`
- `POST /api/v1/Gateway/Discovery`
- `POST /api/v1/Gateway/FX`
- `POST /api/v1/Gateway/Recall`
- `GET /api/v1/Gateway/Operations/{requestMessageId}`
- `GET /api/v1/Gateway/Operations/Unresolved?limit=50`

Technical admission is separate from the final business outcome. Preserve original identifiers across timeouts and retries, and use the operation store and authenticated callbacks to reconcile PAPSS outcomes. See the [bank integration guide](docs/PAPSS_BANK_INTEGRATION_GUIDE.md) for full request, response and callback contracts.

Operational endpoints:

- `GET /health`
- `GET /metrics`
- `GET /api/v1/transactions`
- `GET /api/v1/iso-messages`
- `GET /api/v1/StatusMessages`
- `GET /api/v1/Dashboard/*`
- `GET /api/v1/Participants/*`
- `GET /api/v1/Logs/*`
- `GET /api/v1/Configurations/*`
- `GET /api/v1/Adapter`
- `POST /api/v1/SomQR/*`

A Postman collection is available at [SIPSConnectTest.postman_collection.json](SIPSConnectTest.postman_collection.json).

---

## Testing

Run the package test suite:

```bash
dotnet test Packages/Packages.sln -c Debug -v minimal
```

Targeted API test scripts are available in [SIPS.Connect/TestScripts](SIPS.Connect/TestScripts).

---

## Documentation

- [PAPSS Activation Runbook](docs/PAPSS_ACTIVATION_RUNBOOK.md)
- [PAPSS Bank Integration Guide](docs/PAPSS_BANK_INTEGRATION_GUIDE.md)
- [PAPSS Participant Adapter](docs/PAPSS_PARTICIPANT_ADAPTER.md)
- [PAPSS Operation Store Configuration](docs/PAPSS_OPERATION_STORE_CONFIG.md)
- [Auditor Navigation Guide](docs/reviewer-navigation-guide.md)
- [Audit Readiness Q&A](docs/q-and-a-readiness.md)
- [Repository Structure Overview](docs/REPOSITORY_STRUCTURE.md)
- [QR Integration Guide](docs/integration_guide.md)
- [PKI-Off Hardening Evidence](Packages/SIPS.Docs/Artifacts/PKIOffHardening.md)

---

## Official Container Releases and Team Publishing Guide

SPS Connect (previously SIPS Connect) is built on GitHub Actions and published to GitHub Container Registry. Release **2.0.0** is available for **Linux AMD64 and ARM64**:

```bash
docker pull ghcr.io/sps-sips/sps-connect:2.0.0
```

Banks should use an approved numbered release or its exact digest. `latest` follows the most recently published stable release; `edge` is for development builds.

- **Banks:** [Bank Container Image Guide](docs/BANK_CONTAINER_INSTALLATION.md) — download, registry access, the verified `2.0.0` digest, deployment checks and rollback.
- **SPS release team:** [Container Publishing and Team Release Checklist](docs/CONTAINER_PUBLISHING.md) — version selection, GitHub Actions publishing, verification and bank handover.
- **Build evidence:** [Successful 2.0.0 release workflow](https://github.com/SPS-SIPS/sips-connect/actions/runs/42590322976).

To request the next release from the reviewed `main` commit, use an unused version, for example:

```bash
bash SIPS.Connect/build.command 2.0.1
```

This command requires an authenticated GitHub CLI and starts the build on GitHub. Follow the run to completion and verify the published digest before handing the version to banks. Numbered releases cannot be replaced by the publishing workflow. Publishing an image does not deploy it or activate PAPSS.
