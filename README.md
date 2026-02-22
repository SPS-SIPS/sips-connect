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

## ⚙️ Getting Started

### Prerequisites
- **Docker** & **Docker Compose**
- **.NET 8.0 SDK** (for local development)

### Quick Start
1.  **Clone & Configure**:
    ```bash
    git clone https://github.com/SPS-SIPS/SIPS.git
    cd SIPS/SIPS.Connect
    cp .env.example .env
    ```
2.  **Launch Infrastructure**:
    ```bash
    docker-compose up -d
    ```
3.  **Access Documentation**:
    Swagger/OpenAPI documentation is available at `http://localhost:8080/swagger` when the service is running.

---

*Prepared for World Bank Technical Assessment - Somalia SIPS Modernization (Feb 2026)*
