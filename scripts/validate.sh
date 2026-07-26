#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SDK_VERSION="$(
  python3 - <<'PY' "$ROOT/global.json"
import json
import sys
from pathlib import Path

print(json.loads(Path(sys.argv[1]).read_text())["sdk"]["version"])
PY
)"

candidate_dotnets=()
if [[ -n "${DOTNET_ROOT:-}" && -x "${DOTNET_ROOT}/dotnet" ]]; then
  candidate_dotnets+=("${DOTNET_ROOT}/dotnet")
fi
if [[ -x "${HOME}/.dotnet/dotnet" ]]; then
  candidate_dotnets+=("${HOME}/.dotnet/dotnet")
fi
if command -v dotnet >/dev/null 2>&1; then
  candidate_dotnets+=("$(command -v dotnet)")
fi

DOTNET_BIN=""
for candidate in "${candidate_dotnets[@]}"; do
  if (cd "$ROOT" && "$candidate" --version >/tmp/netpulse-dotnet-version.$$ 2>/tmp/netpulse-dotnet-error.$$); then
    if [[ "$(cat /tmp/netpulse-dotnet-version.$$)" == "$SDK_VERSION" ]]; then
      DOTNET_BIN="$candidate"
      break
    fi
  fi
done
rm -f /tmp/netpulse-dotnet-version.$$ /tmp/netpulse-dotnet-error.$$

if [[ -z "$DOTNET_BIN" ]]; then
  cat >&2 <<EOF
Required .NET SDK $SDK_VERSION was not found.

This repository pins SDK $SDK_VERSION in global.json with rollForward disabled.
Install that SDK or expose it with PATH/DOTNET_ROOT, for example:

  export PATH="\$HOME/.dotnet:\$PATH"
  scripts/validate.sh
EOF
  exit 1
fi

echo "Using .NET SDK $SDK_VERSION: $DOTNET_BIN"
cd "$ROOT"

"$DOTNET_BIN" restore DQOPR.NetPulse.sln
"$DOTNET_BIN" build DQOPR.NetPulse.sln --configuration Release
"$DOTNET_BIN" test DQOPR.NetPulse.sln --configuration Release --no-build
"$DOTNET_BIN" format DQOPR.NetPulse.sln --verify-no-changes --verbosity minimal
"$DOTNET_BIN" list DQOPR.NetPulse.sln package --vulnerable --include-transitive

python3 -m ruff check .
python3 -m mypy src/dqopr_netpulse
python3 -m pytest -q
git diff --check
