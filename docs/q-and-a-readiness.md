# SIPS Connect: Audit Readiness Q&A (Unassailable Version)

This document contains refined, "unassailable" answers to common regulator and technical auditor questions, ensuring the SIPS Connect integration is defensible under intense World Bank-level scrutiny.

---

## ❓ Q1: Data Consistency & Split-Brain
> *"If your participant ledger and the CoreBank system disagree on a transaction's status—for example, the ledger says 'Success' but the CoreBank says 'Failed'—how does SIPS Connect resolve this without creating a financial discrepancy for the switch?"*

**🛡️ Unassailable Answer**:
"For the purpose of the national switch, SIPS Connect treats the participant ledger as the authoritative source of truth **after successful CoreBank confirmation and switch acknowledgment.** Once that state is recorded and acknowledged to the IPS, it is immutable for the switch. Any later discrepancy is treated as an internal bank reconciliation issue, not a protocol error. This 'Ledger-as-Truth' model prevents split-brain scenarios where the switch and the participant disagree on finality, ensuring we never mask a settlement failure to the regulator."

---

## ❓ Q2: Concurrent Multi-Return Risk
> *"What prevents a rogue or buggy client from initiating two separate returns for the same original payment, potentially causing a double loss for the bank if both move through the switch?"*

**🛡️ Unassailable Answer**:
"We enforce a strict Multi-Return Prevention Gate anchored on the `OrgnlTxId` (Original Transaction ID). Every outgoing return is checked against the ledger for existing or pending returns **using a database-level uniqueness constraint and INSERT-first logic.** If a match exists, the second request is deterministically rejected with a `409 Conflict`. This ensures a `1:1` relationship between a payment and its return that is structurally guaranteed against race conditions and retry storms."

---

## ❓ Q3: Dependency Governance & Supply Chain
> *"How do you guarantee that your integration gateway doesn't accidentally inherit 'implicit' behaviors or security vulnerabilities from transitive dependencies that weren't intentionally reviewed?"*

**🛡️ Unassailable Answer**:
"We enforce a **Zero-Implicit-Dependency policy** through the build system using `Directory.Build.props`, which applies solution-wide and cannot be overridden per project. Any attempt to introduce an undeclared dependency fails the build. This ensures the policy is continuously enforced and that the dependency graph is 100% intentional and reviewable, rather than just being a point-in-time snapshot."

---
*Last Updated: 2026-02-08*
*Status: Audit-Ready*
