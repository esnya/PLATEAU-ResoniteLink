#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$REPO_ROOT"

dotnet restore Plateau.ResoniteLink.sln --locked-mode
dotnet build-server shutdown
dotnet format whitespace . --folder --verify-no-changes
dotnet build Plateau.ResoniteLink.sln --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Plateau.ResoniteLink.sln --configuration Release --no-restore --verbosity normal -m:1 -p:UseSharedCompilation=false
