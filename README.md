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

## ⚙️ Reviewer Quick Start (Two-Node Topology)

For technical assessment, we provide a **Two-Node Topology** that simulates a real-world interaction between two independent participant banks (**Bank A** and **Bank B**). This topology is essential for verifying end-to-end flows while avoiding database/ledger collisions that occur in single-node loopback setups.

### Why two nodes?
The SIPS Connect gateway enforces strict de-duplication on `(MessageType, TxId)`. In a single-node loopback mode, the same database would attempt to store both the "Outbound" and "Inbound" side of the same transaction with the same ID, causing a database unique constraint violation (`ux_iso_msg_type_txid`). 

The two-node setup provides:
- **Ledger Isolation**: Each node has its own PostgreSQL database.
- **Realistic Routing**: Nodes communicate via standard HTTP mapping over host ports.
- **Collision-Free Logic**: Verification and Transfers flow naturally from one participant to another.

### 1. Startup

Open two separate terminal windows or tabs:

**Node A (Bank A - Port 8080)**
```bash
cp .env.node-a.example .env.node-a
docker compose -f docker-compose.node-a.yml --env-file .env.node-a up -d --build
```

**Node B (Bank B - Port 9080)**
```bash
cp .env.node-b.example .env.node-b
docker compose -f docker-compose.node-b.yml --env-file .env.node-b up -d --build
```

### 2. Peer Routing Matrix

| Flow | Source Node | Target URL |
| :--- | :--- | :--- |
| **A → B** | Node A (8080) | `http://host.docker.internal:9080/api/v1/incoming` |
| **B → A** | Node B (9080) | `http://host.docker.internal:8080/api/v1/incoming` |

### 3. Health Monitoring

Check the health of both gateways to ensure the environments are ready.

| Node | URL | Status (Reviewer Mode) |
| :--- | :--- | :--- |
| **Node A** | `http://localhost:8080/health` | `ok` (PKI checks `skipped`) |
| **Node B** | `http://localhost:9080/health` | `ok` (PKI checks `skipped`) |

---

## 🔍 Compliance & Verification Toggles

To facilitate isolated review, each node uses "Reviewer Toggles" via its `.env` file:

### 🏦 CoreBank Integration Overlay
Each node points to its own local virtual CoreBank system within its isolated Docker network.
- **Node A**: `COREBANK_BASE_URL=http://sips-corebank-a:8080`
- **Node B**: `COREBANK_BASE_URL=http://sips-corebank-b:8080`

### 🔓 Disabling PKI Signing (`WithoutPKI`)
By default, `WITHOUT_PKI=true` is enabled for reviewers. This allows message inspection and flow verification without managing X.509 signing certificates for every message.
- **Effect**: Cryptographic signing is bypassed; health checks for SIPS Core and Certificates are marked as `skipped`.

### 🔐 Multi-Instance Port Mapping
| Node | Service | Port (Host) |
| :--- | :--- | :--- |
| **A** | Connect HTTP / HTTPS | `8080` / `9443` |
| **A** | PostgreSQL | `5432` |
| **B** | Connect HTTP / HTTPS | `9080` / `10443` |
| **B** | PostgreSQL | `6432` |

---

### 🧪 Postman Quick Validation

A ready-to-use Postman collection is included at `SIPSConnectTest.postman_collection.json`.

1. **Import** the collection into Postman.
2. **Run Configuration first** (`{{baseUrl}}/config`) to initialize environment variables:
   - `baseUrl` (Node A: `http://localhost:8080/api/v1`, Node B: `http://localhost:9080/api/v1`)
   - `api_key`, `api_secret`, `agent`, and sender defaults.
   - > [!IMPORTANT]
   - > Ensure `baseUrl` includes the `/api/v1` suffix (e.g., `http://localhost:8080/api/v1`).
3. **Run flows**: Execute `Verify`, `Transfer`, `Status`, and `Return` requests to validate end-to-end participant-to-participant flows.
4. **Audit & Debug**: Use `StatusMessages` and `iso-messages` requests to inspect the underlying ISO 20022 message store and response signals.

---

*Prepared for World Bank Technical Assessment - Somalia SIPS Modernization (Feb 2026)*
### 🧪 Verification
For detailed evidence of the system's resilience in Reviewer Mode, including automated test results for PKI-off stability, see the [PKI-Off Hardening Evidence](Packages/SIPS.Docs/Artifacts/PKIOffHardening.md).
