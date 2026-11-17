# Security hardening and deployment changes

These changes require a rebuild/redeployment. They do not rotate credentials, modify existing database/Keycloak accounts, change a running deployment or rewrite Git history. Use the [credential inventory](CREDENTIAL_ROTATION_2026-11-14.md) to revoke the exposed values at their receiving services.

## Production startup

`ASPNETCORE_ENVIRONMENT=Production` (also Staging and other non-Development environments) now requires:

- PKI enabled; all configured switch, bank callback, credential-discovery and token URLs use HTTPS and contain no embedded username/password.
- An HTTPS Keycloak authority, valid issuer validation, and either complete callback OAuth client credentials or a nondefault bank callback API pair. Known public test/default passwords are rejected for callbacks and the database.
- A loadable Data Protection certificate with its private key on Linux. Configure `DataProtection:CertificatePath`, `DataProtection:CertificatePassword` and optionally `DataProtection:KeyDirectory` (default `/app/keys` when running in `/app`). Windows can use DPAPI instead.
- An encrypted existing Data Protection key ring. Merely configuring a certificate does not encrypt old plaintext key files. Startup rejects plaintext `masterKey` elements outside Development.

**Do not delete the old key ring to get past validation.** That can make existing encrypted configuration unrecoverable. Keep a restricted offline recovery copy, use the existing ring and matching application/purpose to recover/re-protect the necessary configuration in a controlled process, and verify recovery before replacing it with a protected ring. Existing plaintext backups must be inventoried and retired under the deployment's backup policy. No live key migration was attempted.

Current local `.env*` files still contain development defaults: this change does not invent new passwords or reset a persisted database. Changing `POSTGRES_PASSWORD` in Compose is not a password reset for an existing PostgreSQL data volume.

## Container and network changes

The Docker image now runs as **UID/GID 1654**, executes `dotnet` directly, and updates the system CA store during the build. Custom CA certificates must be added in a derived image **before** `update-ca-certificates`, not mounted into a writable system trust store at application startup.

Existing writable bind mounts and certificate/key files need suitable ownership/permissions for UID 1654. Do not make them world-readable/writable. Prepare the deployment copies/volumes with the deployment operator; local files were not chowned. Existing reviewer Compose ports now bind to loopback, including the database and mock-corebank ports. The mock-corebank receives only its own API credentials rather than the entire root environment file.

[docker-compose.production.yml](../docker-compose.production.yml) is a **standalone template**, not an override to combine with reviewer files. It sets Production, keeps the database unpublished, drops capabilities, uses a read-only application root filesystem, and uses named writable key/log volumes. It requires:

- `SIPS_IMAGE`: a reviewed, rebuilt image digest; scan its operating-system packages before release.
- `SIPS_CONFIG_PATH`, `SIPS_ADAPTER_PATH`, `SIPS_CERTS_PATH`: absolute paths to reviewed deployment files readable by UID 1654. The config must satisfy the startup requirements above. It is mounted read-only, so HTTP configuration-editing endpoints cannot persist changes.
- `DB_USER`, `DB_PASSWORD`, `DB_NAME`: deployment-managed PostgreSQL settings. Provision a least-privileged application role through the database administrator rather than using the bootstrap superuser for normal traffic. The bundled database service is an infrastructure template, not a complete database provisioning/backup strategy.
- `DATA_PROTECTION_CERTIFICATE_PASSWORD`: delivered through the deployment secret store/environment, never checked into Git.

Serve the loopback HTTP port through a TLS ingress on the host. A containerized ingress needs an explicitly designed shared network; `127.0.0.1` inside another container does not reach the host application. Direct HTTPS is also available through configured Kestrel endpoints; production certificate files must be provisioned separately. Protect connections, backup storage, monitoring tokens and operational access in the actual environment.

## Diagnostics and secret administration

- `/health` and `/metrics` require an authenticated principal with the **`monitoring`** role. Create a dedicated monitoring service identity; existing generic API keys do not get that role automatically. For an API-key monitoring identity set `IncludeDefaultRoles: false` and `Roles: ["monitoring"]` so it receives no baseline payment/transaction permissions. Omitting `IncludeDefaultRoles` preserves the old key behavior; existing restricted settings are preserved when rotating a key through an update that omits the flag. Do not grant payment/configuration permissions to monitoring just to scrape metrics. The health endpoint remains a detailed diagnostic response and can include operational balance information for this authorized role.
- Secret management now uses the same **`configuration`** role as other configuration administration. `GET /api/admin/secrets/{key}`, `POST .../decrypt` and `POST .../decrypt-all` return 404 outside Development. Plaintext persistence is independently blocked in the underlying bulk-decryption tool.
- `encrypt-all` traverses JSON objects and arrays, updates values structurally, preserves API-key identifiers and unrelated equal strings, writes through a same-directory temporary file, and creates **no plaintext backup**. Unix output files use mode 0600. Bootstrap `DataProtection`/`Kestrel` secrets and connection strings are reported in `skippedKeys`: supply them through the deployment secret store; encrypting them with the same key ring would prevent startup or be unsupported by their consumers. Do not interpret partial encryption as protection of skipped fields.
- The unused standalone secret CLI (which built an unprotected separate key ring and accepted plaintext in arguments) and unused broken AES helper/converter were removed. Use the supported secret service/tool with the application's configured Data Protection provider. Database encryption at rest remains an infrastructure responsibility.
- Database error detail is disabled in the storage registration even if a connection string enables it. Sensitive EF parameter logging and the reviewed raw message logs are disabled. Existing logs/caches/backups still need access and retention review.

## Continuous checks

Container publishing now depends on a reusable security workflow that:

1. Audits direct/transitive NuGet packages and fails on known advisories **or unavailable vulnerability data**.
2. Runs focused authentication/secret-handling/startup tests and XAdES conformance tests.
3. Uses a digest-pinned Gitleaks image to scan all committed files (including archive contents) and all reachable commits. The workflow also runs weekly to refresh dependency checks.

Only public XML signature reference IDs and one illustrative key identifier have narrowly scoped scanner exceptions. Documentation credentials were converted to angle-bracket placeholders. Historical exposures are not silently allowlisted: the CI gate covers the current snapshot and all reachable history, while the pre-cleanup audit remains recorded in the rotation inventory. Revoke historical credentials even when the current-tree scan is clean.

Hosted secret scanning/push protection, production credential revocation, TLS/identity-provider/payment-switch interoperability, existing registry images and production log/backup inspection still require access to those systems. No deployment was performed by this change.

## Domestic switch certificate identity

Domestic IPS verification now requires `Xades:DomesticSwitchCertificateSha256`, an array of SHA-256 hashes of the **DER-encoded switch signing certificate**. A CA chain alone does not identify the national switch. Missing, malformed or nonmatching pins fail closed; non-Development startup rejects an absent or malformed list. Do not pin a participant certificate, a private-key hash, a PEM file's text hash, or a certificate learned solely from an incoming message.

Obtain the public signing certificate and confirm its SHA-256 fingerprint with the switch operator through an authenticated, independent channel. Compute the DER fingerprint locally with:

```sh
openssl x509 -in switch-signing.pem -outform DER | openssl dgst -sha256
```

Use the resulting 64 hexadecimal characters, without colons or the command's label. The standalone production template requires `DOMESTIC_SWITCH_CERT_SHA256` for the current certificate. For rotation, provision both current and independently approved replacement fingerprints as array entries (`Xades__DomesticSwitchCertificateSha256__0`, `__1`), roll out the configuration, verify interoperability, then remove the retired fingerprint after the agreed overlap. Do not enable unsigned mode to bypass pinning.

## Runtime configuration and logging

All HTTP configuration mutations are denied outside Development, even for a configuration administrator. Apply PKI, endpoints, callback credentials, CORS, allowed-host and API-key changes through the reviewed deployment process and restart. Read endpoints mask secrets, including callback OAuth client secrets. Bulk encryption through HTTP is Development-only; production secret provisioning must occur in the controlled deployment process. Denials record actor and action without request bodies; Development mutation outcomes are recorded separately. Restrict access to audit logs and deploy with immutable configuration mounts.

Application logs omit raw balance/authentication/participant responses, customer names and account aliases in the reviewed paths. The sample file sink rolls daily and at 10 MiB, retaining at most 31 files (size rolls can shorten the effective time window). The logs API requires the dedicated logs role. Operators must enforce filesystem/collector access, central retention and deletion of old sensitive logs; changing application code does not cleanse existing logs or external collectors.

Reviewer Compose files now default to PKI enabled. Unsigned local testing requires an explicit `WITHOUT_PKI=true` in Development. These files remain Development examples, not production profiles. Use the separate Production template and satisfy its startup checks.

## Regression evidence and release gate

Controlled tests cover a signed pacs.008 document hidden before a modified direct payment document, duplicate documents/headers/signatures/IDs, alternate `xml:id`, prohibited DTDs, a different participant signing under the same CA, missing/malformed pins, overlapping certificate rotation and revocation. Existing legacy vendor and PAPSS signature fixtures remain in the signature suite.

`BouncyCastle.NetCore 2.2.1` was replaced by the official [BouncyCastle.Cryptography 2.7.0 package](https://www.nuget.org/packages/BouncyCastle.Cryptography/2.7.0). Resolve and audit the complete dependency graph during release; dependency scanning does not prove absence of all cryptographic implementation flaws. Certificate validity now uses an injectable clock, defaulting to the system clock, so historical fixture tests can validate their intended trust failure without disabling expiry enforcement.

The application suite has an existing generated-schema initialization race under parallel execution. Run its full regression suite with `-- xUnit.ParallelizeTestCollections=false`. Do not report a parallel failure as a security remediation success.

Credential revocation, trusted switch pin provisioning, participant deployment verification and hosted retained-history cleanup remain operator actions. These operational actions are separate from source remediation.

Validation recorded on 15 November 2026: 31 signature tests passed; the complete application suite passed 286 tests in serial mode; core tests passed 237 with 7 existing skips. Both controlled C-01/C-02 tests fail against the original verifier because it accepts the attack, and pass after remediation. The pacs.008 control confirms that the ordinary payment parser reads an instructed amount of 999 from the altered direct document while the original verifier accepts the unchanged wrapped document originally signed with amount 1. The signer-identity control uses a separately generated participant certificate chaining to the same test CA.

The solution audit covered eight projects and reported zero known vulnerable direct/transitive packages or audit problems. A source snapshot containing tracked files and the new security files was scanned with the digest-pinned Gitleaks image: 12.69 MB scanned, zero findings with the existing narrow public-identifier allowlists. Repository hygiene positive/negative controls and the current sample scan passed. These are local checks; no deployment or hosted workflow run was performed as part of this validation.

The final source-only C-04 pass also removes remote error bodies, amount-mismatch details, QR/parser exception messages and adapter conversion exception text from application logs. Detailed operator recall reasons stay in the authorized audit record rather than the general application log. Five focused privacy tests cover success/error balance responses, conversion failures, remote login error text and propagated certificate-lookup error text.

Repository checks found zero matches for the six known exposed credential values and zero complete JWTs across all local Git objects. A separate full-history Gitleaks scan reported zero findings.

The final source image was rebuilt successfully and its executed UID verified as 1654. The build context now excludes macOS resource forks as well as deployment secrets. These checks validate the source and build artifacts.
