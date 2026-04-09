#!/usr/bin/env bash

set -euo pipefail

current_tag="${1:?current tag is required}"
previous_tag="${2:-}"
output_path="${3:?output path is required}"

mkdir -p "$(dirname "${output_path}")"

cat > "${output_path}" <<EOF
## Summary

- Automated release for ${current_tag}.
- GitHub Releases are the canonical changelog for Plateau.ResoniteLink.
- \`Plateau.ResoniteLink-cli-${current_tag}.zip\` is a framework-dependent asset and requires .NET 10.

EOF

if [[ -z "${previous_tag}" ]]; then
  cat >> "${output_path}" <<'EOF'
## Initial release context

This tag starts the formal GitHub Releases-based changelog for Plateau.ResoniteLink.

Highlights before this first tagged release:
- Added the live CLI pipeline for streaming PLATEAU CityGML datasets into Resonite through ResoniteLink.
- Hardened direct archive imports with explicit remote archive validation, cache reuse, and pre-validation of import requests.
- Added DEM heightmap terrain generation, placement fixes, and live asset reuse improvements.
- Expanded automated coverage around CLI parsing, import validation, dataset discovery, DEM layout, and live send behavior.

EOF
else
  cat >> "${output_path}" <<EOF
_Changes below were generated automatically from merged pull requests and commits since \`${previous_tag}\`._

EOF
fi
