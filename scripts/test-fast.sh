#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TEST_FILTER="${PLATEAU_TEST_FILTER:-Category!=Slow}"
TEST_VERBOSITY="${PLATEAU_TEST_VERBOSITY:-minimal}"

cd "$REPO_ROOT"

test_args=(
  Plateau.ResoniteLink.sln
  --configuration Release
  --no-restore
  --verbosity "$TEST_VERBOSITY"
  -m:1
  --disable-build-servers
  -p:UseSharedCompilation=false
)

if [[ -n "$TEST_FILTER" ]]; then
  test_args+=(
    --filter
    "$TEST_FILTER"
  )
fi

dotnet test "${test_args[@]}"
