#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$REPO_ROOT"

dotnet test Plateau.ResoniteLink.sln \
  --configuration Release \
  --no-restore \
  --verbosity minimal \
  --filter "Category!=Slow" \
  -m:1 \
  -p:UseSharedCompilation=false
