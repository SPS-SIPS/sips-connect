# Contributing

SIPS Connect is owned by SOMALI PAYMENT SWITCH (SPS) LTD and is maintained as
part of the Somali Instant Payment System technology ecosystem.

By contributing, contributors agree that their contributions are submitted
under the Apache License 2.0 unless a separate written agreement with SPS says
otherwise.

## Contribution Requirements

- Keep payment finality, idempotency, replay, return handling, settlement, and
  cryptographic trust behavior explicit and tested.
- Do not include BPC/SmartVista proprietary source, confidential configuration,
  production data, credentials, certificates, or private keys.
- Use environment variables or deployment-specific secret stores for secrets.
- Include focused tests for changes to transaction lifecycle, ISO 20022
  parsing/building, signature verification, callbacks, persistence, retries, or
  recovery workers.
- Update documentation when behavior, configuration, or operational procedures
  change.

## High-Risk Areas

Changes in these areas require senior review:

- Payment status/finality mapping.
- Duplicate detection and deterministic replay.
- Return and reversal logic.
- Signature, certificate, public key, and PKI behavior.
- CoreBank callback semantics.
- SAF, timeout, retry, and recovery behavior.
- Database migrations and persistence constraints.
