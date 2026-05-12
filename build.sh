#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

BUILD_PROJECT_FILE="$SCRIPT_DIR/build/_build.csproj"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_NOLOGO=1

if ! command -v dotnet >/dev/null 2>&1; then
    echo ".NET SDK not found on PATH. Install it from https://dot.net or check global.json." >&2
    exit 1
fi

# Use `-` switches (not `/`) so Git Bash on Windows doesn't path-translate them.
dotnet build "$BUILD_PROJECT_FILE" -nodeReuse:false -p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet
dotnet run --project "$BUILD_PROJECT_FILE" --no-build -- "$@"
