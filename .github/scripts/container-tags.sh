#!/usr/bin/env bash
set -euo pipefail

# Inputs arrive through the environment, never through shell interpolation.
owner="$(printf '%s' "$GITHUB_REPOSITORY_OWNER" | tr '[:upper:]' '[:lower:]')"
image="ghcr.io/$owner/sps-connect"
version=""
case "$GITHUB_EVENT_NAME" in
  workflow_dispatch)
    if [[ "$GITHUB_REF" != "refs/heads/main" ]]; then
      echo "Release builds must be dispatched from main." >&2
      exit 1
    fi
    version="${RELEASE_VERSION#v}"
    ;;
  push)
    if [[ "$GITHUB_REF" == refs/tags/v* ]]; then
      version="${GITHUB_REF#refs/tags/v}"
    elif [[ "$GITHUB_REF" != "refs/heads/main" ]]; then
      echo "Unsupported publishing ref: $GITHUB_REF" >&2
      exit 1
    fi
    ;;
  pull_request) ;;
  *) echo "Unsupported event: $GITHUB_EVENT_NAME" >&2; exit 1 ;;
esac

if [[ "$GITHUB_EVENT_NAME" == "workflow_dispatch" || "$GITHUB_REF" == refs/tags/* ]]; then
  if [[ ! "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
    echo "Release version must be a stable MAJOR.MINOR.PATCH, for example 2.0.0." >&2
    exit 1
  fi
fi

{
  echo "image=$image"
  echo "version=$version"
  echo 'tags<<IMAGE_TAGS'
  if [[ -n "$version" ]]; then
    echo "$image:$version"
    echo "$image:latest"
  elif [[ "$GITHUB_EVENT_NAME" == "push" ]]; then
    echo "$image:edge"
  fi
  echo "$image:sha-$GITHUB_SHA"
  echo 'IMAGE_TAGS'
} >> "$GITHUB_OUTPUT"
