# Walkthrough: PKI-Off Mode Hardening

I have completed the hardening of the "Reviewer Mode" (PKI-Off) to ensure the SIPS Connect gateway is resilient, stable, and reports a clean "ok" status even without SIPS Core connectivity.

## Changes Implemented

### 1. No-Op Repository Client
I implemented a `NoOpRepositoryHttpClient` to handle outbound discovery calls when PKI is disabled.
- **Benefit**: Removes the risk of `UriFormatException` from empty discovery URLs and prevents unintended network traffic.
- **File**: `SIPS.Connect/Services/Internal/NoOpRepositoryHttpClient.cs`

### 2. DI & Fail-Fast Validation
Updated the DI registration to conditionally use the No-Op client or the real HTTP client.
- **Fail-Fast**: If `WITHOUT_PKI=false` (PKI enabled), the gateway now validates all required discovery fields (`Core:BaseUrl`, `Core:PublicKeysRepUrl`, `Core:Username`, etc.).
- **Robustness**: Replaced direct `new Uri()` parsing with `Uri.TryCreate` for both `Core:BaseUrl` and `Core:PublicKeysRepUrl` to ensure malformed URLs result in an actionable error message instead of a generic crash.
- **File**: `SIPS.Connect/Config/Service.cs`

### 3. Gated Health Checks
The `HealthCheckService` now skips components that depend on external PKI or Identity services when in Reviewer Mode.
- **Skipped Components**: `sips-core`, `xades-certificate`, `balance-status`, and `keycloak`.
- **Status**: These are marked as `skipped` in the `/health` payload but do **not** degrade the overall system status.
- **File**: `SIPS.Connect/Services/HealthCheckService.cs`

### 4. Schema Alignment & Migration Proof
I resolved a critical schema mismatch where the `uetr` column was missing from the `isomessages` table.

**Migration Executed Successfully (Logs):**
```bash
[22:00:44 INF] Applying migration '20260222185945_AddUetrColumnToIsoMessages'.
[22:00:44 INF] Executed DbCommand (2ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
ALTER TABLE isomessages ADD uetr text;
[22:00:44 INF] Executed DbCommand (13ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
CREATE INDEX ix_iso_msg_uetr ON isomessages (uetr);
```
- **Migration Files**: 
  - `Packages/SIPS.PostgreSQL/Migrations/20260222185945_AddUetrColumnToIsoMessages.cs`
  - `Packages/SIPS.PostgreSQL/Migrations/20260222185945_AddUetrColumnToIsoMessages.Designer.cs`
- **Result**: Database operations (like Payee Verification) are now compatible with the EF model.

### 5. Configuration & Docs
- **Compose**: Updated `docker-compose.yml` with optional PKI discovery variables.
- **Env**: Updated `.env.example` with documented discovery sections.
- **Docs**: Added a dedicated "PKI-Off Mode" section to `README.md`.

## Verification Status

1. **Startup**: `sips-connect` starts without `UriFormatException` when `WITHOUT_PKI=true`.
2. **Health**: `GET http://localhost:8080/health` returns `{"status": "ok", ...}` with `skipped` PKI components.
3. **Flow**: Payee Verification (`/api/v1/Gateway/Verify`) is active and no longer crashes on database schema errors.
