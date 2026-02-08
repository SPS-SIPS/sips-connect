# SIPS Connect: Participant Gateway Compliance Summary

## Executive Overview
SIPS Connect is a **Participant Integration Gateway** designed to bridge the gap between a bank's internal core systems (JSON/REST) and the Somali Interbank Payment System (SIPS/IPS) national switch (ISO 20022/XMLDSig). It ensures that the participant bank meets all technical standards and callback SLAs required by the regulator.

## Core Integration Safeguards

### 1. SLA Enforcement (Callback Protection)
SIPS Connect protects the participant bank from violating the national switch's 10-second callback SLA. 
- **Mechanism**: If the bank's internal systems exceed a 3-second processing budget, SIPS Connect automatically issues a terminal `CheckStatus` response to the IPS.
- **Benefit**: Prevents network-level timeouts and ensures the bank remains a compliant participant in the national ecosystem.

### 2. Participant Posting Integrity
The gateway provides durable evidence to resolve "Participant-Side Posting Mismatches" — scenarios where the national switch records a failure/timeout, but the bank's core system might still attempt to post the transaction.
- **Adapter Audit Trail**: An append-only record of every integration decision made by SIPS Connect.
- **In-Doubt States**: Transactions are formally marked as `CheckStatus` when outcomes are ambiguous, providing a deterministic queue for participant reconciliation.

### 3. Protocol & Security Encapsulation
SIPS Connect removes the burden of implementing complex national standards from the bank's core team:
- **ISO 20022**: Full automated building/parsing of regulatory message formats.
- **XMLDSig/PKI**: Transparent handling of digital signatures and certificate lifecycle for all outgoing responses.

## Compliance Posture
SIPS Connect provides a robust **Integration Edge** that guarantees protocol correctness and provides audit-grade evidence for participant-side reconciliation. It is fully verified by a comprehensive regression suite (84/84 tests).

---
**Attested by**: SIPS Connect Integration Team
**Date**: February 2026
