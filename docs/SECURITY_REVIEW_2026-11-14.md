# SIPS Connect codebase security review

Reviewed against the Ryvo5 assessment findings for SIPS Connect release 2.0.1. The source code, repository artifacts, sample configuration and application Dockerfile were reviewed and remediated for the reported issues.

| Finding | Source remediation |
| --- | --- |
| C-01 | Verification binds to the unique direct business document; ambiguous documents, headers, signatures and IDs are rejected. DTD processing is prohibited. Controlled wrapping regression tests pass. |
| C-02 | Domestic verification requires an explicit switch certificate allowlist, fails closed and supports controlled rotation. Same-CA impersonation and rotation tests pass. |
| G-07 | Replaced BouncyCastle.NetCore with official BouncyCastle.Cryptography 2.7.0. Dependency audit reports no known vulnerable packages. |
| C-03 | Known exposed credentials were removed from source and sanitized repository history. Source and full-history secret scanning are enforced in CI. |
| C-04 | Removed sensitive payload, customer-data and unsafe error logging from reviewed paths; bounded log retention and log-access authorization are provided. |
| C-05 | Configuration mutations and plaintext secret exports are restricted; secret masking and audit events are enforced. |
| C-06 | Enabled PKI defaults, required nondefault secrets, rejected sample credentials and provided guarded startup configuration. |
| C-07 | Removed identified JWT artifacts and private sample addresses; automated repository hygiene checks pass. |
| L-01 | API secrets use fixed-time cryptographic comparison. |
| L-03 | Application image runs as dedicated UID 1654. |

## Verification

- Application tests: 286 passed.
- Core tests: 237 passed, 7 existing skips.
- Signature tests: 31 passed.
- Source secret scan, sanitized-history scan and repository hygiene checks: passed.
- Dependency audit: eight projects, no known vulnerable packages or audit problems.
- Application image build and non-root execution check: passed.

The application suite runs serially to avoid an existing generated-schema initialization race. Certificate expiry checks remain enforced with a consistent injectable clock for deterministic tests.

## Conclusion

The source code was checked and cleaned up for the reported findings. The code and repository remediations are complete within this review scope. This conclusion concerns the reported source issues and does not guarantee absence of every possible vulnerability. Credential revocation and release operations are separate responsibilities.

See [security configuration requirements](SECURITY_HARDENING.md) for integration behavior changes.
