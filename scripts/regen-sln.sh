#!/usr/bin/env bash
# Regenerate DailySummary.sln from the projects on disk, using the dotnet CLI
# (canonical GUIDs, no hand-editing). Run from anywhere; it cd's to the repo root.
set -euo pipefail

cd "$(dirname "$0")/.."

rm -f DailySummary.sln
dotnet new sln -n DailySummary

# Add every project under src/ and tests/.
mapfile -t projects < <(find src tests -name '*.csproj' | sort)
dotnet sln DailySummary.sln add "${projects[@]}"

echo "Regenerated DailySummary.sln with ${#projects[@]} projects:"
printf '  %s\n' "${projects[@]}"
