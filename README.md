# Somali Instant Payment System (SIPS)

The **Somali Instant Payment System (SIPS)** repository contains **SIPS Connect**, the participant-side integration gateway for the Somali national instant-payment ecosystem.

SIPS Connect bridges the SIPS/SVIP switch with participant CoreBank systems. It handles ISO 20022 message orchestration, CoreBank JSON adaptation, transaction persistence, digital signing, signature verification, public-key lookup, idempotency, return safety, timeout recovery, SomQR services, dashboards, health checks, metrics, and operational configuration.

---

## Ownership, Authorship & Licensing

This repository is owned by **SOMALI PAYMENT SWITCH (SPS) LTD**.

**Technical leadership and implementation:** SIPS Connect was designed, built, deployed, and productionized by Abdulshakur Ahmed Aided ([@hanadderia](https://github.com/hanadderia)) within the Somali Payment Switch institutional context.

This repository is released under the **Apache License 2.0**. See [LICENSE](LICENSE) and [NOTICE](NOTICE).

Release governance is documented through:

- [NOTICE](NOTICE) - institutional copyright, attribution, and trademark notice.
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) - third-party and product-reference notices.
- [SECURITY.md](SECURITY.md) - vulnerability reporting and sensitive-material policy.
- [CONTRIBUTING.md](CONTRIBUTING.md) - contribution rules and high-risk review areas.
- [CODEOWNERS](CODEOWNERS) - institutional maintainers.

---

## Architecture Overview

SIPS Connect is a modular .NET platform composed of:

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

SIPS Connect includes payment-system controls designed to protect transaction correctness and operational traceability:

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

SIPS Connect can route the existing participant operations to either the domestic IPS or PAPSS. Each deployment represents one participant bank. The bank identity is read from `Xades:BIC`; it is not selected from an HTTP identity claim or request payload.

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
    "Participants": {
      "local-bank": {
        "Enabled": true,
        "Bic": "<SAME-AS-XADES-BIC>",
        "LocalCountry": "<ISO-3166-ALPHA-2>",
        "SendingCurrencies": ["<ISO-4217>"],
        "AllowedOperations": [
          "Verification",
          "Payment",
          "Status",
          "Return",
          "Readiness",
          "Discovery",
          "Fx"
        ],
        "CallbackMappingProfile": "papss-callback-v1",
        "CallbackUrl": "https://<BANK-INTERNAL-HOST>/<CALLBACK-PATH>"
      }
    }
  }
}
```

`PapssFacing:Participants` is required when PAPSS is enabled. Its key must exactly match the authenticated API-key/JWT principal name, and its `Bic` must match `Xades:BIC`. The entry owns local country, permitted sending currencies, operations and callback configuration. `SpsPolicy:AllowedLocalInstruments` is an optional SPS restriction; PAPSS-supported instruments are always learned from signed discovery/readiness data.

Payment destination BIC and, where needed, receiver currency are transaction selections. SIPS Connect performs signed, correlated Discovery and Readiness lookups for every PAPSS payment, derives receiver country, and validates current status, online eligibility, currencies and payment schemas. No per-destination deployment entry is used.

All PAPSS operations use one service ingress:

```text
POST https://<PAPSS-SERVICE-HOST>/sips/messages
Content-Type: application/xml
signed ISO 20022 using WpSipsPapss
```

The existing `/Verify`, `/Payment`, `/Status`, and `/Return` JSON mappings accept an optional `rail` field. Omitting it preserves domestic IPS behavior. `/Readiness`, `/Discovery`, and `/FX` are PAPSS-only and require `rail=PAPSS`.

### Safe enablement

1. Deploy SIPS Connect with `PapssFacing:Enabled=false`.
2. Verify normal SmartVista/IPS traffic and health.
3. Install the local WP-SIPS signing certificate/private key and the PAPSS verification trust chain.
4. Configure `/sips/messages`, its allowed host, the expected PAPSS identity, security profile, and authenticated local participant facts. Add an SPS instrument restriction only if deliberately required.
5. Load the PAPSS request and callback JSON adapter mappings.
6. Set `PapssFacing:Enabled=true` and restart SIPS Connect so startup validation runs.
7. Verify readiness, then test `Verification` before enabling financial UAT flows.

Use `Sps.Sips.XmlSecurity.Xades` version `1.0.3` or later. Version `1.0.3` keeps the PAPSS identified-envelope signature while removing the PAPSS-only `FPEnvelope/@Id` from legacy IPS messages.

Detailed integration and mapping guidance:

- [PAPSS Bank Integration Guide](docs/PAPSS_BANK_INTEGRATION_GUIDE.md)
- [PAPSS Participant Adapter](docs/PAPSS_PARTICIPANT_ADAPTER.md)
- [XAdES Profile-Separation Evidence](docs/XADES_PROFILE_SEPARATION_EVIDENCE.md)

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

The JSON adapter files define the mapping between CoreBank/local JSON payloads and the internal SIPS Connect DTOs:

- `jsonAdapter.json`
- `jsonAdapter.node-a.json`
- `jsonAdapter.node-b.json`

For Docker-based local simulation, adapter files are mounted into the containers. After changing adapter mappings, restart the relevant SIPS Connect container:

```bash
docker compose -f docker-compose.node-a.yml restart sips-connect-a
docker compose -f docker-compose.node-b.yml restart sips-connect-b
```

---

## API Surface

Primary gateway endpoints:

- `POST /api/v1/gateway/Verify`
- `POST /api/v1/gateway/Payment`
- `POST /api/v1/gateway/Status`
- `POST /api/v1/gateway/Return`
- `POST /api/v1/gateway/Retry/{id}`
- `POST /api/v1/incoming`

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

- [Auditor Navigation Guide](docs/reviewer-navigation-guide.md)
- [Audit Readiness Q&A](docs/q-and-a-readiness.md)
- [Repository Structure Overview](docs/REPOSITORY_STRUCTURE.md)
- [QR Integration Guide](docs/integration_guide.md)
- [PKI-Off Hardening Evidence](Packages/SIPS.Docs/Artifacts/PKIOffHardening.md)
