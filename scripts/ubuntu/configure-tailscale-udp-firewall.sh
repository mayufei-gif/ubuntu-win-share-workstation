#!/usr/bin/env bash
set -euo pipefail

MODE="${1:---check}"
PORT="${TAILSCALE_PORT:-}"

if [[ "$MODE" != "--check" && "$MODE" != "--apply" && "$MODE" != "--rollback" ]]; then
  echo "Usage: sudo bash $0 [--check|--apply|--rollback]" >&2
  exit 2
fi

if [[ -z "$PORT" ]] && command -v tailscale >/dev/null 2>&1; then
  PORT="$(
    timeout 10s tailscale debug prefs 2>/dev/null |
      sed -n 's/.*"Port":[[:space:]]*\([0-9][0-9]*\).*/\1/p' |
      head -n 1
  )"
fi

if [[ -z "$PORT" ]] && command -v ss >/dev/null 2>&1; then
  PORT="$(
    ss -H -lunp 2>/dev/null |
      awk '/tailscaled/ {
        endpoint=$5
        sub(/^.*:/, "", endpoint)
        if (endpoint ~ /^[0-9]+$/) {
          print endpoint
          exit
        }
      }'
  )"
fi

if [[ -z "$PORT" ]]; then
  echo "Could not confirm the Tailscale UDP port. Set TAILSCALE_PORT explicitly." >&2
  exit 3
fi

if ! [[ "$PORT" =~ ^[0-9]+$ ]] || (( PORT < 1 || PORT > 65535 )); then
  echo "Invalid UDP port: $PORT" >&2
  exit 4
fi

echo "TAILSCALE_UDP_PORT=$PORT"

if ! command -v ufw >/dev/null 2>&1; then
  echo "UFW_NOT_INSTALLED_NO_CHANGE"
  exit 0
fi

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Run with sudo for UFW inspection or changes." >&2
  exit 5
fi

case "$MODE" in
  --check)
    ufw status verbose
    if ufw status | grep -Eq "^${PORT}/udp[[:space:]]+ALLOW"; then
      echo "TAILSCALE_UFW_RULE_PRESENT"
    else
      echo "TAILSCALE_UFW_RULE_MISSING"
    fi
    ;;
  --apply)
    ufw allow "${PORT}/udp" comment "Tailscale direct UDP"
    ufw status numbered
    echo "TAILSCALE_UFW_RULE_APPLIED"
    ;;
  --rollback)
    ufw --force delete allow "${PORT}/udp" || true
    ufw status numbered
    echo "TAILSCALE_UFW_RULE_REMOVED"
    ;;
esac

