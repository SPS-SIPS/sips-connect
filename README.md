# Somali Instant Payment System (SIPS)

The **Somali Instant Payment System (SIPS)** is a mission-critical financial infrastructure designed to modernize payment flows across Somalia. 

This repository houses the **SIPS Connect** integration gateway—the central component that bridges the national SIPS SVIP switch with internal banking ledgers and core banking systems.

---

## 🛡️ Auditor & Reviewer Navigation

For technical auditors from the **World Bank** and regulatory bodies, we have prepared specialized guides to streamline the review process:

1.  **[Auditor Navigation Guide](docs/reviewer-navigation-guide.md)**: A structured path to verify security, compliance, and architectural integrity.
2.  **[Audit Readiness Q&A](docs/q-and-a-readiness.md)**: Refined technical answers regarding data consistency, race conditions, and financial safety.
3.  **[Repository Structure Overview](docs/REPOSITORY_STRUCTURE.md)**: A detailed breakdown of the monorepo logic and component separation.

---

## 🏛️ Architecture & Governance

### Monorepo Strategy
SIPS uses a unified **Monorepo** architecture to ensure a single source of truth for all participant gateway components. This enforces deterministic builds and explicit dependency governance.

### Zero-Implicit-Dependency Model
> [!IMPORTANT]
> To prevent supply-chain vulnerabilities and "magic" behaviors, this repository enforces a **Zero-Implicit-Dependency** policy via [Directory.Build.props](Directory.Build.props).
> Transitive project references are disabled; every coupling must be intentional and auditable.

---

## ⚖️ Core Compliance Pillars

The system is built on "unassailable" engineering patterns to ensure financial safety:

-   **The Gold Pattern (Inbound Verification)**: INSERT-first structurally enforced de-duplication within the `IncomingVerificationHandler`.
-   **Multi-Return Prevention**: A strict gate anchored on the `OrgnlTxId` (Original Transaction ID) to prevent double-losses.
-   **Ledger-as-Truth**: The local participant ledger serves as the authoritative source of truth for the national switch, preventing split-brain scenarios between the IPS and Core Banking.
-   **ISO 20022 Compliance**: End-to-end support for ISO 20022 message building, parsing, and cryptographically signed audit trails.

---

## 📁 Repository Breakdown

### 🚀 [SIPS.Connect](SIPS.Connect/)
The primary entry point. A high-performance integration gateway handling API controllers, middleware, and financial orchestration.

### 📦 [Packages](Packages/)
Shared internal libraries providing the heavy lifting:
-   **SIPS.Core**: Central business logic and handler orchestration.
-   **SIPS.PostgreSQL**: High-performance persistence and financial ledger logic.
-   **SIPS.ISO20022**: Regulatory message processing and schema validation.
-   **SIPS.XMLDsig.Xades**: Digital signature implementation for PKI compliance.

---

## ⚙️ Reviewer Quick Start (Minimal Stack)

For auditors who wish to verify the system in an isolated environment, we provide a root-level Docker Compose configuration. This setup starts the **Gateway**, a **Mock CoreBank**, and the **PostgreSQL** database.

### 1. Prerequisites
- **Docker** & **Docker Compose**
- **TLS Certificate**: Place a PKCS#12 certificate (`.pfx`) in the `./certs` directory.

### 2. Startup
Run the following commands from the repository root:
```bash
# 1. Prepare environment variables
cp .env.example .env

# 2. Build and start the stack (3 services)
docker compose up -d --build

# 3. Verify health
docker compose ps
```

### PKI-Off Mode (Reviewer Mode)

By default, the reviewer environment runs with `WITHOUT_PKI=true`. This "Reviewer Mode" is designed for local technical assessment without requiring connection to a central SIPS Core instance.

**Implications of PKI-Off Mode:**
- **Decoupled Discovery**: The gateway does not attempt to connect to SIPS Core discovery services (Live Participants, Balance Status, etc.).
- **No-Op Core Client**: Internal outbound discovery calls are handled by a `NoOpRepositoryHttpClient`, preventing unintended network traffic.
- **Gated Health Checks**: PKI-specific health checks (**Sips Core**, **Xades Certificate**, **Balance Status**) and **Keycloak** checks are automatically marked as `skipped` in the health report.
- **Overall System Status**: These skipped checks do NOT degrade the overall system status, which will remain `ok` as long as the database and local CoreBank integration are healthy.

**Enabling PKI Mode (`WITHOUT_PKI=false`):**
If you wish to test with full PKI and Core discovery enabled:
1. Set `WITHOUT_PKI=false` in `.env`.
2. Provide valid `SIPS_CORE_*` variables in `.env` (see `.env.example`).
3. The gateway will perform a **Fail-Fast Validation** at startup. If any required discovery URLs or credentials are missing, the container will exit with an error.

### Health Monitoring

The gateway exposes a comprehensive health endpoint at `http://localhost:8080/health`.

| Component | Status (Reviewer Mode) | Description |
| :--- | :--- | :--- |
| **database** | `ok` | Connection to PostgreSQL ledger. |
| **corebank** | `ok` | Connectivity to the mock CoreBank system. |
| **sips-core** | `skipped` | SIPS Core discovery endpoint (Gated). |
| **xades-certificate** | `skipped` | Local PKI certificate file check (Gated). |
| **keycloak** | `skipped` | Identity Provider connectivity (Gated). |
| **balance-status** | `skipped` | External balance monitoring (Gated). |

### 3. Service Map
| Service | Image/Source | Description |
| :--- | :--- | :--- |
| **sips-connect** | Local Build | The SIPS Connect Gateway (Middleware under review). |
| **sips-corebank** | `hanad/sips-consumer` | A mock CoreBank system for end-to-end testing. |
| **postgresql** | `postgres:16-alpine` | Authoritative participant ledger and audit store. |

---

## 🔍 Compliance & Verification Toggles

To facilitate isolated review without external dependencies (like the central SIPS Switch), the gateway supports several "Reviewer Toggles" via the `.env` file:

### 🔄 Self-Calling (Loopback) Mode
You can force the gateway to act as its own "switch" by pointing the SIPS endpoint to its own internal incoming handler. This allows end-to-end verification of the financial message lifecycle on a single node.
- **Config**: `SIPS_LOOPBACK_URL=http://sips-connect:8080/api/v1/incoming`

### 🏦 CoreBank Integration Overlay
The gateway endpoints for interacting with the Core Banking System (Verification, Transfer, etc.) can be centralized via a single base URL.
- **Config**: `COREBANK_BASE_URL=http://sips-corebank:8080`
- **Derived Endpoints**: The gateway automatically appends standard paths like `/api/cb/verify` and `/api/CB/Transfer` to this base URL.

### 🔓 Disabling PKI Signing (`WithoutPKI`)
To simplify flow verification without managing X.509 signing certificates for every message, you can bypass the XAdES signature layer.
- **Config**: `WITHOUT_PKI=true`
- **Effect**: The `NativeSigner` will bypass the cryptographic signing process, allowing raw ISO 20022 message inspection.

### 🔐 TLS Configuration
The gateway is configured to start an HTTPS listener on port 443 (Host port `9443` by default).
- **Certificate Path**: `./certs/sips-connect.pfx` (mapped via volume).
- **Configuration**: Managed via the `Kestrel` section in `appsettings.json`.

---

*Prepared for World Bank Technical Assessment - Somalia SIPS Modernization (Feb 2026)*
