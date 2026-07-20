#!/usr/bin/env bash
set -euo pipefail

STATE_ROOT="${STATE_ROOT:-$HOME/.local/share/ubuntu-win-share-agent}"
UNIT_PATH="$HOME/.config/systemd/user/ubuntu-win-nas-upload-agent.service"
CRON_MARKER="# ubuntu-win-nas-upload-agent"
WATCHDOG="$STATE_ROOT/agent-watchdog.sh"
AGENT="$STATE_ROOT/nas_upload_agent.py"

stop_exact_processes() {
  local needle="$1"
  local pid
  local args
  while read -r pid args; do
    if [[ -n "$pid" &&
          "$pid" != "$$" &&
          "$args" == *"$needle"* ]]; then
      kill "$pid" >/dev/null 2>&1 || true
    fi
  done < <(ps -u "$(id -u)" -o pid=,args=)
}

systemctl --user disable --now ubuntu-win-nas-upload-agent.service >/dev/null 2>&1 || true
rm -f "$UNIT_PATH"
systemctl --user daemon-reload >/dev/null 2>&1 || true

if command -v crontab >/dev/null 2>&1; then
  existing="$(crontab -l 2>/dev/null || true)"
  printf '%s\n' "$existing" | grep -Fv "$CRON_MARKER" | crontab - || true
fi

stop_exact_processes "$WATCHDOG"
stop_exact_processes "$AGENT --serve"
rm -rf -- "$STATE_ROOT"
echo "NAS_UPLOAD_AGENT_UNINSTALL_OK"
