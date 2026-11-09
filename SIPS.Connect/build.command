#!/usr/bin/env bash
set -euo pipefail

# Builds run on GitHub; the workflow publishes both architectures to GHCR.
version="${1:-2.0.0}"
if [[ ! "$version" =~ ^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
  echo "Usage: bash build.command [MAJOR.MINOR.PATCH]" >&2
  exit 1
fi

gh workflow run container.yml \
  --repo SPS-SIPS/sips-connect \
  --ref main \
  -f "version=$version"
printf 'Requested SPS Connect %s build on GitHub. Check the repository Actions page.\n' "$version"
