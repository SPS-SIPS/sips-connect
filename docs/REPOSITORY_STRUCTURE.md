# SIPS Repository Structure

## Overview
This repository uses a **Monorepo** architecture to unify the Somali Interbank Payment System (SIPS) components. This structure ensures a single source of truth, deterministic builds, and explicit dependency governance across the entire participant gateway ecosystem.

## Directory Layout

### Core logic & Integration
- **[/SIPS.Connect](file:///Users/maven/source/SIPS/SIPS.Connect)**: The main entry point for the Participant Integration Gateway. Contains the API controllers, middleware, and application hosting logic.
- **[/Packages](file:///Users/maven/source/SIPS/Packages)**: Shared internal libraries.
    - `SIPS.Core`: Central business logic and handler orchestration.
    - `SIPS.PostgreSQL`: Data access layer and financial ledger persistence.
    - `SIPS.ISO20022`: ISO 20022 message building, parsing, and schema validation.
    - `SIPS.Adapter`: Protocol translation and external system integration.
    - `SIPS.Emv`: EMV tag processing and card-data utilities.
    - `SIPS.XMLDsig.Xades`: Digital signature implementation for regulatory compliance.

### Examples & Dev Tools
- **[/Examples](file:///Users/maven/source/SIPS/Examples)**: Implementation examples for consumer integration and deployment.

### User Interface
- **[/sps-dashboard-ui](file:///Users/maven/source/SIPS/sps-dashboard-ui)**: The operational dashboard interface.

## Separation of Concerns
> [!IMPORTANT]
> **Authority Invariant**: All financial transaction logic and regulatory compliance mechanisms reside strictly within `SIPS.Connect` and the `Packages/` libraries. 
> The UI code (`sps-dashboard-ui`) and Examples are non-authoritative components used for observability and demonstration only.

## Dependency Governance
The repository enforces a **Zero-Implicit-Dependency** model using `Directory.Build.props`. Transitive project references are disabled to ensure all coupling is intentional and auditable.
