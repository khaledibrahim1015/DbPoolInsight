

# Explain shebang
# #!/bin/bash           # Hardcoded path - may fail on some systems
# #!/usr/bin/env bash   # Flexible - finds bash in PATH
# #!/bin/sh             # Uses system shell (often bash in POSIX mode)
# #!/usr/bin/env python3 # Works for any interpreter, not just bash



#!/usr/bin/env bash

# =============================================================================
# scripts/build-push.sh
# Build and push the efcore-api Docker image
#
# Usage:
#   ./scripts/build-push.sh <registry> [tag]
#   ./scripts/build-push.sh docker.io/myuser          # tags as :latest
#   ./scripts/build-push.sh docker.io/myuser 1.2.0    # tags as :1.2.0
#   ./scripts/build-push.sh ghcr.io/myorg/efcore-api 1.2.0
# =============================================================================

# This sets strict bash execution options:
# -e: Exit immediately if any command fails
# -u: Treat unset variables as errors
# -o pipefail: Make pipelines fail if any command in the pipeline fails
set -euo pipefail


# If you run: ./script.sh
# $1 is unset/empty → Error message shown, script exits
# REGISTRY=${1:?"Usage: $0 <registry> [tag]"}
# Output: line X: 1: Usage: ./script.sh <registry> [tag]
# If you run: ./script.sh myregistry.com
# $1 = "myregistry.com" → REGISTRY = "myregistry.com"
# Equivalent code without this syntax
# if [ -z "${1:-}" ]; then
#     echo "Usage: $0 <registry> [tag]"
#     exit 1
# fi
# REGISTRY="$1"

REGISTRY=${1:?"Usage: $0 <registry> [tag]"}
TAG=${2:-latest}
IMAGE="$REGISTRY/efcore-api:$TAG"


SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
echo "DEBUG SCRIPT_DIR: '$SCRIPT_DIR'"
# Find the project root by looking for a marker (like Deployments folder)
# Look for the Docker directory by walking up until we find it
find_project_root() {
    local current_dir="$SCRIPT_DIR"

    while [[ "$current_dir" != "/" && "$current_dir" != "c:" && "$current_dir" != "C:" ]]; do
        if [ -d "$current_dir/Deployments/Docker" ]; then
            echo "$current_dir"
            return 0
        fi
        current_dir="$(dirname "$current_dir")"
    done

    echo "ERROR: Could not find project root" >&2
    exit 1  # ← fail loudly instead of guessing
}
PROJECT_ROOT="$(find_project_root)"
echo "DEBUG PROJECT_ROOT: '$PROJECT_ROOT'"  # ← add this

echo "Building image: $IMAGE"
echo "  Context:    $PROJECT_ROOT"
echo "  Dockerfile: Docker/Dockerfile.efcoreapi"
echo ""


docker build \
  --file "$PROJECT_ROOT/Deployments/Docker/Dockerfile.efcoreapi" \
  --target final \
  --tag "$IMAGE" \
  --label "build.date=$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --label "build.tag=$TAG" \
  "$PROJECT_ROOT"



echo ""
echo "Pushing $IMAGE..."
docker push "$IMAGE"

echo ""
echo "Done! Update your Kustomize overlay to use this image:"
echo "  images:"
echo "    - name: "$REGISTRY"/efcore-api"
echo "      newTag: \"$TAG\""
