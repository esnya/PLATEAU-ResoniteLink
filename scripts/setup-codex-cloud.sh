#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DOTNET_DIR="${DOTNET_ROOT:-$HOME/.dotnet}"
DOTNET_VERSION="${DOTNET_VERSION:-10.0.105}"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet was not found on PATH; installing SDK ${DOTNET_VERSION} into ${DOTNET_DIR}"
  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --version "$DOTNET_VERSION" --install-dir "$DOTNET_DIR"
fi

if ! command -v dotnet >/dev/null 2>&1; then
  export PATH="$DOTNET_DIR:$PATH"
fi

echo "Using dotnet: $(dotnet --version)"

bash "$REPO_ROOT/scripts/verify-ci.sh"
