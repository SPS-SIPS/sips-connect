# Evidence: PKI-Off Mode Hardening

This document provides evidence for the resilience improvements made to the SIPS Connect "Reviewer Mode" (PKI-off), ensuring the system remains stable even when security certificates and keys are missing.

## 1. Problem Statement
Previously, the `SIPS.XMLDsig.Xades` package would always attempt to load certificate files (`.pem`, `.key`, `.pfx`) from disk during startup, regardless of whether `WithoutPKI` was enabled. This led to `FileNotFoundException` crashes in environments without pre-provisioned certificates.

## 2. Solution: NoOpCertificateService
Implemented a `NoOpCertificateService` that satisfies the `ICertificateService` interface without touching the file system.

### Key Implementation Details
- **Lazy/Conditional Resolution**: `ICertificateService` is registered via a DI factory that checks `XadesOptions.WithoutPKI`.
- **Contract Safety**: Throwing `InvalidOperationException` for all PKI-dependent members to prevent silent failures.
- **Diagnostics**: Logs a warning with a 🛡️ shield emoji on initialization to confirm the bypass is active.

## 3. Automated Verification
The following regression tests were committed to `Packages/SIPS.Core.Tests/Tests/NoOpCertificateServiceTests.cs`:
- `ShouldResolveNoOpCertificateService_WhenWithoutPKI_IsTrue`: Ensures the factory resolves the correct type.
- `NativeVerifier_ShouldFunction_WhenWithoutPKI_IsTrue_AndFilesMissing`: Verifies the verifier gracefully skips PKI logic without crashing and returns safe defaults.
- `NoOpCertificateService_ShouldThrow_OnPKIDependentMembers`: Ensures contract compliance.

### Test Results
```text
[xUnit.net 00:00:00.09]   Starting:    SIPS.Core.Tests
[xUnit.net 00:00:00.13]   Finished:    SIPS.Core.Tests
  SIPS.Core.Tests test succeeded (0.7s)
Test summary: total: 4, failed: 0, succeeded: 4, skipped: 0, duration: 0.7s
```

## 4. Manual/Runtime Verification
Verified in a live Docker container by removing the `certs` directory and starting the gateway with `WITHOUT_PKI=true`.

**Log Evidence:**
```text
[08:57:00 WRN] 🛡️ NoOpCertificateService initialized. Certificate file loading is SKIPPED.
```
