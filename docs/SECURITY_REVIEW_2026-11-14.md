# Security review — 14 November 2026

Reviewed revision: `5efd87a`, plus the local fixes described below. **This project cannot be certified free of vulnerabilities or historical secret exposure.** Confirmed issues were found and several are fixed in the working tree. Credential rotation and deployment checks remain necessary. No production system was changed, no credential was tested against a service, and Git history was not rewritten.

## Scope and evidence

- Reviewed the .NET application, authentication/authorization setup, incoming signature boundary, secret handling, database logging, Docker configuration, and build workflow.
- Ran the NuGet vulnerability audit against all eight projects in `SIPS.sln`, including transitive packages. Separate Connect and XML signature test projects were also restored during validation.
- Scanned all 5,939 reachable Git objects across 989 reachable commits (`git rev-list --objects --all`), examining blobs for credential assignments, JWTs, common cloud/GitHub token formats and PEM private-key markers. Manually inspected relevant configuration and realm-import structures to catch credentials stored under generic fields such as `value`.
- Checked local `.env` files and certificate/key-file tracking and permissions. The local `.env`, `.env.node-a`, `.env.node-b`, PFX files and Data Protection key files are untracked. The three local environment files were changed from mode 0644 to 0600. Their contents were not copied into this report.
- This was a source/configuration review and heuristic secret scan, not a penetration test or a dedicated scanner certification. Remote branches not present locally, unreachable/deleted Git objects, published images, package registries, deployed services, hosted CI logs, databases, and compressed document contents were not audited. Existing untracked user documents were left untouched.

## Findings and disposition

| Severity | Finding | Status |
| --- | --- | --- |
| High | Non-placeholder API credentials and a Core password exist in reachable history. | **Rotation required.** See the exact [rotation inventory](CREDENTIAL_ROTATION_2026-11-14.md). Validity and production use were not verified. |
| High | Both tracked realm imports provisioned an enabled administrator with a publicly known default password and no forced password change. | Removed imported users. Existing identity-provider accounts must be changed or disabled separately. |
| High | Cryptography dependencies carried four high and one moderate advisory. | Updated `System.Security.Cryptography.Xml` from 9.0.19 to 9.0.21; its transitive Pkcs dependency also resolves to 9.0.21. Subsequent solution audit reports zero known vulnerable packages. |
| High | If API-secret decryption failed, `ApiKeyProvider` retained ciphertext and the handler accepted it as the shared secret. | Failed keys are now omitted; the handler independently rejects encrypted text, blank credentials and repeated credential headers, and uses fixed-time hash comparison for secrets. |
| High, conditional | JWT discovery allowed HTTP, enabling signing-key substitution if deployed with an HTTP authority and an attacker could tamper with the connection. Issuer validation could also be disabled. | HTTPS metadata and issuer validation are required outside Development. Current tracked authority already uses HTTPS. |
| Medium | EF sensitive-data logging was always enabled; several message handlers logged complete payment XML, bank responses or serialized records. | Removed automatic EF sensitive-data logging and reviewed raw body/record logs; retained correlation IDs, status and lengths. Existing logs and upstream exception text still require access controls and retention review. |
| Medium | A configured Data Protection certificate that was missing or failed to load silently left keys unprotected. | Certificate errors fail registration; production startup now also rejects a missing Linux certificate and existing plaintext key-ring files. |
| Medium | Encryption-tool plaintext backups were excluded from Git but could enter Docker build context/caches. | Added Docker exclusions for appsettings backups and `.bak` files. Existing images/caches and runtime backups need separate inspection. |
| Low | Two complete JWTs remained in the tracked Postman collection. | Replaced with `{{access_token}}`. Both expired on 29 December 2025; no current bearer-token validity was established. Their historical claims remain visible in Git history. |
| Low | Endpoint-specific rate limiting ran before routing. | Moved rate limiting after routing so endpoint policies can be selected. |
| Low | Secret-management responses could be cached. | Added `Cache-Control: no-store` response configuration. |

Dependency advisory references: [XML CVE-2026-96958](https://github.com/advisories/GHSA-r955-8q5j-3h83), [XML CVE-2026-96960](https://github.com/advisories/GHSA-7hqp-7c5m-4vq6), [XML CVE-2026-96959](https://github.com/advisories/GHSA-c399-9853-6jmj), [Pkcs CVE-2026-84008](https://github.com/advisories/GHSA-846f-f764-76mx), [Pkcs CVE-2026-70162](https://github.com/advisories/GHSA-2fmv-rv2c-59x7). Microsoft documents that [HTTPS metadata should only be disabled for development](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.jwtbearer.jwtbeareroptions.requirehttpsmetadata).

## Follow-up hardening and rotation inventory

The [exact credential inventory](CREDENTIAL_ROTATION_2026-11-14.md) identifies two Core login accounts (`zirat`, `repo`), two distinct historical **core-bank callback** API pairs, the two default imported Keycloak administrators, the historical `Test` incoming API entry, locally reused test core-bank credentials, and the default PostgreSQL role password in the three local environments. It gives field names, recipient context, evidence blobs and fingerprints without plaintext values.

**Correction:** historical `constants/constants.ts` referenced `NEXT_PUBLIC_API_Key` and `NEXT_PUBLIC_API_Secret`; it did not embed literal credentials. The initial heuristic scanner mistook environment-variable references for values. No separate browser API credential pair was established. Published frontend bundles were not examined.

The follow-up used Gitleaks 8.30.1 against all reachable history and the current source snapshot, including archive contents. The raw history scan returned 66 findings, mostly duplicate commits, documentation placeholders and public XML IDs, plus the already identified historical credentials/JWTs. Current documentation headers now use explicit placeholders. The scanner configuration has narrowly scoped exceptions for public signature/key **identifiers**, not private keys or live credential values. The final current-tree scan reported zero findings after the documented public-identifier exceptions.

Additional fixes are described in [the deployment hardening guide](SECURITY_HARDENING.md):

- Non-Development startup rejects unsigned mode, non-HTTPS credential/payment URLs, embedded URL credentials, incomplete/default callback authentication, default database passwords, missing key-encryption configuration, and existing plaintext key rings.
- Plaintext secret retrieval/decryption and bulk plaintext persistence are blocked outside Development. The administrative role matches the existing `configuration` role.
- Bulk encryption handles arrays and escaping structurally, preserves identifiers/unrelated strings, writes atomically with mode 0600 on Unix, creates no plaintext backup, and reports bootstrap/connection-string secrets that must be managed separately.
- Detailed health and metrics require a dedicated `monitoring` role. Database error detail is disabled in storage registration.
- The Docker image runs as UID 1654 and updates CA trust at build time. Reviewer ports bind to loopback; a separate production Compose template requires deployment-managed settings and provides a read-only application filesystem, dropped capabilities and unpublished database ports.
- Container publishing depends on automated dependency auditing, security regression tests, signature conformance checks and a digest-pinned Gitleaks current/new-commit scan. The workflow also runs weekly.
- The unused broken AES helper/converter and unused standalone secret CLI were removed. Nested key/certificate/log directories and additional key-store formats are excluded from the Docker context.

### What still requires action outside this repository

1. Revoke/reset the credentials in the inventory wherever they were provisioned. Existing accounts, databases, sessions, registry images and deployed secrets were not changed.
2. Migrate/re-protect existing plaintext key rings and safely manage recovery copies; do not delete old keys without recovering/re-protecting the dependent configuration.
3. Rebuild/redeploy, provision certificate/volume permissions for UID 1654, assign the `monitoring` role to a dedicated monitoring identity, and configure the TLS ingress and authenticated bank integration.
4. Inspect/scan deployed OS/container images, old logs/backups/build caches and published frontend bundles. Provision least-privileged database application roles and enable hosted secret scanning/push protection. Access to those environments was not used here.
5. Resolve the existing certificate-fixture failure and intermittent XML-schema concurrency failures noted below; this work does not certify the broader suite as green.

## Verification

The entries immediately below describe the first review pass; follow-up results are appended separately.

- Evidence: [NuGet before](security-review/nuget-before.json), [NuGet after](security-review/nuget-after.json). Five distinct advisories before; zero vulnerable packages reported after.
- Authentication/Data Protection regressions: **12 passed**. Core banking suite: **237 passed, 7 skipped**.
- Application suite with xUnit test-collection parallelism disabled: **262 passed, 1 failed** (263 total). The failing captured-certificate fixture also fails on the untouched baseline. The final default-parallel run had **252 passed, 11 failed**, including intermittent XML-schema initialization errors; an earlier default-parallel run had 261 passed and only the baseline fixture failure.
- XAdES conformance tests: **17 passed** after the dependency update.
- The untouched baseline application suite had **250 passed, 1 failed**. The failure is `PapssRecallCapturedFixtureTests.Captured_payload_passes_certificate_pinning_and_stops_only_at_chain_of_trust_to_the_ephemeral_test_ca`; certificate validity checking fails before the chain-of-trust assertion. It also occurs after these fixes.
- Repeated default-parallel application runs intermittently hit `XObjectsCore` schema static-initialization exceptions (`FacetsChecker` / missing `String` dictionary entry). Disabling test-collection parallelism avoided those failures in the final diagnostic run. This isolates the behavior but does not fix or establish the root cause of the concurrency issue.
- PostgreSQL integration tests and live TLS/identity-provider/payment-switch interoperability were not executed.

Rebuild/redeployment and the operational actions above are required for the fixes to affect a running service.

## Follow-up verification

- Security regressions: **25 passed**, including real HTTP checks that anonymous callers get 401, ordinary bank identities get 403 on diagnostics, and monitoring-only keys cannot use payment permissions.
- Core suite: **237 passed, 7 skipped**. Signature conformance: **17 passed**.
- Full application suite with test-collection parallelism disabled: **275 passed, 1 failed** (276 total). The failure remains the same captured-certificate fixture demonstrated on the untouched baseline.
- Transitive dependency audit with unavailable-audit-data/advisory warnings treated as errors: **passed**. No package references were added by the follow-up.
- Gitleaks 8.30.1 current-source snapshot scan, including archive traversal to depth 2: **zero findings** with the narrowly scoped public-identifier exceptions. The full-history findings remain subject to credential revocation.
- Final Docker image built successfully with pulled .NET base images; it contains .NET/ASP.NET **8.0.34** and runs as UID **1654** with a direct `dotnet` entrypoint. Runtime smoke checks verified writable application key/log directories and a nonwritable system CA directory. This is not an OS-vulnerability scan or a live banking interoperability test.
- Production and main Compose templates validated with interpolation disabled; workflow YAML parsed; `git diff --check` passed. Hosted workflow execution has not occurred yet.
- No credentials were revoked, no running service was redeployed, no existing key ring was migrated.
