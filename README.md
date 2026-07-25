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
- `ISO20022`
- `Emv`
- `Keycloak`
- `CorsPolicies`
- `ApiKeys`

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
