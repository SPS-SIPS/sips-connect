# Auditor & Reviewer Navigation Guide

## Overview
This guide provides a structured path for technical auditors to verify the security, compliance, and architectural integrity of the SIPS.Connect project.

## 1. Governance & Dependency Graph
*   **Central Policy**: [Directory.Build.props](file:///Users/maven/source/SIPS/Directory.Build.props)
    *   *Verification*: Confirms all dependencies are explicit and non-transitive.
*   **Project Definitions**: [SIPS.Connect.csproj](file:///Users/maven/source/SIPS/SIPS.Connect/SIPS.Connect.csproj), [SIPS.Core.csproj](file:///Users/maven/source/SIPS/Packages/SIPS.Core/SIPS.Core.csproj)
    *   *Verification*: Review physical project references to the `/Packages/` source.

## 2. Inbound Payment Hardening ("Gold Pattern")
The core de-duplication and financial safety logic resides in the `IncomingVerificationHandler`.

*   **Entry Point**: [IncomingController.cs:Post](file:///Users/maven/source/SIPS/SIPS.Connect/Controllers/IncomingController.cs)
*   **Primary Handler**: [IncomingVerificationHandler.cs](file:///Users/maven/source/SIPS/Packages/SIPS.Core/Services/IncomingVerificationHandler.cs)
    *   *Pattern*: INSERT-First structurally enforced de-duplication.
    *   *Idempotency*: Replay-wait logic for concurrent requests.
*   **PostgreSQL Persistence**: [IncomingRecorder.cs](file:///Users/maven/source/SIPS/Packages/SIPS.PostgreSQL/Gateway/IncomingRecorder.cs)
    *   *Verification*: Strict lowercase table/column naming and raw SQL atomicity.
*   **Return Gating**: [OutgoingReturnTransactionHandler.cs](file:///Users/maven/source/SIPS/Packages/SIPS.Core/Services/OutgoingReturnTransactionHandler.cs)
    *   *Verification*: Multi-return prevention anchored strictly on **OrgnlTxId** (Original TxId).
*   **Investigation Authority**: [IncomingTransactionStatusHandler.cs](file:///Users/maven/source/SIPS/Packages/SIPS.Core/Services/IncomingTransactionStatusHandler.cs)
    *   *Verification*: Inbound `pacs.028` resolved strictly via **Ledger-as-Truth** (local DB only).

## 3. Financial Safety & Safety Policy
*   **Safety Ledger**: [IncomingRecorder.cs:AppendAuditLedgerEventAsync](file:///Users/maven/source/SIPS/Packages/SIPS.PostgreSQL/Gateway/IncomingRecorder.cs)
    *   *Verification*: Atomic JSONB appending of audit events (e.g., `CheckStatusRaised`).
*   **Safety Policy**: [safety_policy.md](file:///Users/maven/.gemini/antigravity/brain/ba0a69e6-dcff-4332-85fc-a30acee0e4d7/safety_policy.md) (Brain Artifact)
    *   *Verification*: Review the invariants for "No Double Credit" and "Phantom Credit Mitigation".

## 4. SmartVista & ISO 20022 Compliance
*   **Protocol Mapping**: [protocol_mapping_matrix.md](file:///Users/maven/.gemini/antigravity/brain/ba0a69e6-dcff-4332-85fc-a30acee0e4d7/protocol_mapping_matrix.md)
*   **XML Integrity**: [AdminMessageBuilder.cs](file:///Users/maven/source/SIPS/Packages/SIPS.ISO20022/Helpers/AdminMessageBuilder.cs)
    *   *Verification*: Explicit namespace prefixes (`header:`, `document:`) for auditor-friendly parsing.
*   **Signing Logic**: [NativeSigner.cs](file:///Users/maven/source/SIPS/Packages/SIPS.XMLDsig.Xades/Services/NativeSigner.cs)

## 5. Automated Verification
*   **Test Suite**: [SIPS.Core.Tests](file:///Users/maven/source/SIPS/Packages/SIPS.Core.Tests)
*   **Release Pipeline**: Run `dotnet pack -c Release` to verify artifact integrity.

---
*Prepared for World Bank Technical Assessment - Feb 2026*
