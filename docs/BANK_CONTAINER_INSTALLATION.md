# SPS Connect bank container image guide

SPS Connect (previously SIPS Connect) supports domestic SIPS and cross-border PAPSS integration. The official container image is hosted in the Somali Payment Switch GitHub organization:

```text
ghcr.io/sps-sips/sps-connect:2.0.0
```

Use the numbered version approved for your bank. `edge` is a development image. `latest` changes when a release is published and should not be used as a fixed production reference. For an exact deployment artifact, use the release digest supplied by SPS.

## Access and download

If the package is private, SPS must grant the bank's authorized GitHub identity read access. The identity needs a classic personal access token with `read:packages`, authorized for organization SSO where required. Store the token in the bank's approved secret-management system. Public packages can be pulled without authentication.

```bash
# Private packages only; GHCR_READ_TOKEN comes from your secret-management process.
printf '%s' "$GHCR_READ_TOKEN" | docker login ghcr.io -u <GITHUB_USERNAME> --password-stdin

docker pull ghcr.io/sps-sips/sps-connect:2.0.0
docker buildx imagetools inspect ghcr.io/sps-sips/sps-connect:2.0.0
```

The release supports Linux AMD64 and ARM64. Docker selects the matching platform automatically. The token is a registry download credential, not a Gateway API credential.

## Update an existing deployment

In your bank-managed Compose service, change only the image reference:

```yaml
services:
  sps-connect:
    image: ghcr.io/sps-sips/sps-connect:2.0.0
    # Retain the existing environment, ports, volumes, networks and restart settings.
```

This fragment illustrates the image setting; it is not a complete deployment configuration. Remove a local `build:` setting for that service if your deployment should use registry images exclusively. Retain the actual service name used by your bank.

Before production deployment:

1. Record the current image version/digest and deployment configuration for rollback.
2. Download the approved release and verify the digest against the SPS release handover.
3. Review the release scope and any database migration requirements. Image publication itself does not apply or approve database changes.
4. Validate in UAT with your existing runtime settings, credentials, certificates and bank endpoint mappings.
5. Verify `/health`, domestic flows, and PAPSS flows only where PAPSS is activated for your deployment.
6. Schedule the bank-approved deployment and in-flight transaction handling procedure.

After completing these checks, use your existing deployment process. A Compose example is:

```bash
docker compose pull <EXISTING_SERVICE_NAME>
docker compose up -d --no-deps <EXISTING_SERVICE_NAME>
```

The image does not contain bank production secrets. Supply database connections, signing keys, certificates, API credentials and callback configuration through the approved runtime deployment mechanisms. Changing the image registry does not activate PAPSS automatically.

## Support and rollback

Supply SPS support with the image version/digest, environment, timestamp, affected operation references and sanitized logs. Keep passwords, tokens, private keys and sensitive customer data out of support evidence.

For rollback, use the previous approved image/digest and the agreed bank change procedure. Review database compatibility and reconcile in-flight operations before switching versions. Do not delete transaction records or generate replacement payment identifiers to recover an image deployment.

SPS provides technical connectivity and message processing. Settlement, funding and liquidity remain the responsibility of the participant bank and its PAPSS or sponsor arrangements.
