#!/usr/bin/env bash

echo "Current directory: $(pwd)"
echo "Script path: ${BASH_SOURCE[0]}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
echo "SCRIPT_DIR: $SCRIPT_DIR"

REPO_ROOT="$(dirname "$(dirname "$SCRIPT_DIR")")"
echo "REPO_ROOT: $REPO_ROOT"

echo -e "\nDirectory structure:"
echo "SCRIPT_DIR parts:"
echo "$SCRIPT_DIR" | tr '/' '\n' | nl

echo -e "\nREPO_ROOT parts:"
echo "$REPO_ROOT" | tr '/' '\n' | nl
