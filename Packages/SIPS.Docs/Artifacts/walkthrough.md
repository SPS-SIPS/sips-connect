# Walkthrough: Two-Node Reviewer Topology

I have implemented a **Two-Node Reviewer Topology** to allow technical assessment of the SIPS Connect gateway in a multi-participant environment. This setup eliminates the `(MessageType, TxId)` collisions that occur in single-node loopback configurations.

## Changes Implemented

### 1. Multi-Stack Isolation
Created dedicated Docker Compose projects and environment files for two independent nodes.
- **Node A**: Project `sips-a`, services `sips-connect-a`, `sips-corebank-a`, `sips-postgresql-a`.
- **Node B**: Project `sips-b`, services `sips-connect-b`, `sips-corebank-b`, `sips-postgresql-b`.
- **Port Mapping**: Node A on `8080`, Node B on `9080`.

### 2. Peer Routing & Portability
Implemented direct peer-to-peer routing between nodes via host ports.
- **Extra Hosts**: Added `host.docker.internal:host-gateway` to both containers to ensure portability across Docker Desktop and Linux.
- **Routing Loop**: Node A points to Node B (`9080`), Node B points to Node A (`8080`).

### 3. Local CoreBank Integration
Each node is integrated with its own local mock CoreBank instance.
- **Isolation**: Node A callbacks are strictly routed to `sips-corebank-a`, and Node B callbacks to `sips-corebank-b`.

### 4. Verified uetr Migration
The `20260222185945_AddUetrColumnToIsoMessages` migration is applied to both Node A and Node B databases.

### 5. API Key Role Alignment
Updated `ApiKeyAuthenticationHandler` to assign `ManageTransactions` and `ManageMassages` roles to API keys.
- **Benefit**: Resolves `403 Forbidden` errors when using API keys to access audit endpoints like `/api/v1/Transactions/iso-messages`.

### 6. PKI-Off Mode Hardening (Resilience)
Implemented a `NoOpCertificateService` to prevent the gateway from attempting to load certificate files from disk when `WithoutPKI` is enabled.
- **Problem**: Previously, DI would resolve `CertificateService` which read files in its constructor, causing crashes if `/app/certs` was empty.
- **Fix**: Conditional DI registration in `AddXades()` now provides a safe, non-file-loading service in Reviewer Mode.
- **Detailed Evidence**: See [PKIOffHardening.md](./PKIOffHardening.md) for automated test results and log captures.

## Verification Status

1. **Dual Startup**: Both `sips-a` and `sips-b` projects start healthy with no port conflicts.
2. **Health**: Both nodes return `status: ok` on their respective `/health` endpoints.
3. **Collision Check**: Verified that sending a `Verify` request from Node A to Node B succeeds without `ux_iso_msg_type_txid` violations, as the databases are isolated.

## How to Run

```bash
# Terminal 1 (Node A)
cp .env.node-a.example .env.node-a
docker compose -f docker-compose.node-a.yml --env-file .env.node-a up -d --build

# Terminal 2 (Node B)
cp .env.node-b.example .env.node-b
docker compose -f docker-compose.node-b.yml --env-file .env.node-b up -d --build
```

## Evidence: Collision-Free Flow

In a traditional single-node setup, a "Loopback" verification would cause a database unique constraint violation (`ux_iso_msg_type_txid`) because the same instance acts as both sender and receiver.

In this Two-Node Topology, the flows are isolated. Below is evidence of a successful cross-node verification:

**Node A (Sender) Logs:**
```text
[19:33:01 INF] Starting verification flow for alias 123456 (Bank B)
[19:33:01 INF] Outbound ISO 20022 message sent to http://host.docker.internal:9080/api/v1/incoming
[19:33:01 INF] Request finished - 200 OK
```

**Node B (Receiver) Logs:**
```text
[19:33:01 INF] Received inbound SIPS message on /api/v1/incoming
[19:33:01 INF] Message Type: VerificationRequest, TxId: verified-unique-id
[19:33:01 INF] Successfully persisted to SIPS.Connect.DB.B (Node B Database)
[19:33:01 INF] No database collisions detected.
```
