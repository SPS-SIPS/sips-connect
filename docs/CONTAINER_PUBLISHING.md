# SPS Connect container publishing

SPS Connect is built on GitHub Actions and published to GitHub Container Registry (GHCR) as:

```text
ghcr.io/sps-sips/sps-connect
```

The workflow is `.github/workflows/container.yml`. It uses the existing `SIPS.Connect/Dockerfile`, repository-root build context, and both `linux/amd64` and `linux/arm64`, matching the platforms in the previous local build command. The initial GHCR release is a fresh build of current source tagged `2.0.0`; it is not a byte-for-byte copy of `hanad/sips-connect:2.0.0`.

## Tags and versioning

| Trigger | Published image tags |
|---|---|
| Pull request targeting `main` | Both architectures built for validation; nothing published |
| Push to `main` | `edge` and `sha-<full-commit-sha>` |
| Push a Git tag such as `v2.0.1` | `2.0.1`, `latest`, and `sha-<full-commit-sha>` |
| Manual run from `main` with version `2.0.0` | `2.0.0`, `latest`, and `sha-<full-commit-sha>` |

Release versions use stable `MAJOR.MINOR.PATCH` numbers, starting at `2.0.0`. Increase the patch for compatible fixes, minor for compatible features, and major for incompatible changes. Prerelease/build-metadata suffixes and leading zeroes are rejected. The image release version is independent of the existing application assembly version in `SIPS.Connect.csproj`; this migration does not change that assembly version.

Normal main builds never change `latest` or numbered release tags. The workflow checks existing package versions and refuses to replace an existing numbered release. Publish a new version for subsequent releases; retry a failed build with the same version only if it was not already published. External/manual registry writes are outside this workflow's protection.

`latest` means the most recently published stable release, not necessarily the highest semantic version. Publish releases in increasing version order. Commit tags identify source; numbered versions identify releases. For deployment reproducibility, use the published digest recorded in the workflow summary. The same source can produce a different digest when rebuilt with updated base images or dependencies.

## First publication as 2.0.0

1. Commit the workflow, tag helper, build-context exclusions and this guide to `main`, then push them to GitHub.
2. In repository **Settings > Actions > General**, ensure Actions and the referenced official GitHub/Docker actions are permitted by repository and organization policy.
3. Under **Actions**, select **Build and publish SPS Connect**, then **Run workflow**.
4. Select `main`, retain version `2.0.0`, and start the run. It builds the selected main commit on GitHub and publishes `2.0.0` and `latest` after a successful build.
5. Confirm the image digest in the run summary and inspect the manifest for both architectures:

   ```bash
   docker buildx imagetools inspect ghcr.io/sps-sips/sps-connect:2.0.0
   ```

6. Validate the image in the bank's test environment with its existing configuration, volumes, networking, certificates and health checks before changing production deployments.

The workflow authenticates using the automatically supplied `GITHUB_TOKEN` with `contents: read` and `packages: write`. No Docker Hub credentials or additional publishing PAT are needed for a new package in this repository's namespace. The source label links the package to this repository. If a package with this name already exists, ensure this repository has Actions write access to it. Repository/organization policy may restrict package creation or token permissions. See [GitHub's publishing guidance](https://docs.github.com/en/packages/managing-github-packages-using-github-actions-workflows/publishing-and-installing-a-package-with-github-actions).

New packages are private by default. Keep them private for authenticated deployments or change visibility deliberately in the package settings if anonymous pulls are required. For private packages, grant the deployment identity package read access and use a classic PAT with `read:packages` (and organization SSO authorization where required). See [GitHub Container Registry access](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry).

```bash
# For private packages, supply GHCR_READ_TOKEN through your secret-management process.
printf '%s' "$GHCR_READ_TOKEN" | docker login ghcr.io -u <GITHUB_USERNAME> --password-stdin
docker pull ghcr.io/sps-sips/sps-connect:2.0.0
```

## Subsequent releases

From the reviewed release commit on `main`:

```bash
git tag v2.0.1
git push origin v2.0.1
```

Alternatively, dispatch the workflow on `main` with version `2.0.1`. Use one release mechanism per version. A manually published version does not create a Git tag or GitHub Release. Protect release tags through repository rules so only authorized maintainers can publish them. Tag-triggered builds use the tagged commit; manual builds are restricted to `main`.

## Deployment migration

Change the deployed image reference from:

```text
hanad/sips-connect:2.0.0
```

to:

```text
ghcr.io/sps-sips/sps-connect:2.0.0
```

Retain the existing runtime environment, secrets, volumes, ports and command settings. Repository-root Compose manifests currently build the application locally; they are development/simulation configurations and are not automatically switched to registry pulls by this change. The separate consumer image is also unchanged. The existing `SIPS.Connect/build.command` now dispatches the GitHub workflow instead of building locally or pushing to Docker Hub. With GitHub CLI installed and authenticated, run `bash SIPS.Connect/build.command 2.0.1` from the repository root. The workflow must already exist on `main`, and the command only requests a run; check Actions for its result.

The workflow builds and publishes images; it does not deploy or restart running services. Keep the previous deployment image/digest available for controlled rollback. No production certificates, private keys or environment files should be included in a build; the root `.dockerignore` excludes those local inputs.
