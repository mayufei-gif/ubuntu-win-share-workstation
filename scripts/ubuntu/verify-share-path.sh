#!/usr/bin/env bash
set -euo pipefail

SHARE_PATH="${1:-${SHARE_PATH:-$HOME/C/ubuntu-win}}"

if [[ ! -d "$SHARE_PATH" ]]; then
  echo "SHARE_PATH_MISSING path=$SHARE_PATH" >&2
  exit 1
fi

echo "SHARE_PATH_OK path=$SHARE_PATH"
find "$SHARE_PATH" -maxdepth 1 -type f -name "_ubuntu_win_share_probe_*.txt" -printf "%TY-%Tm-%Td %TH:%TM:%TS %p\n" 2>/dev/null | sort | tail -n 10
