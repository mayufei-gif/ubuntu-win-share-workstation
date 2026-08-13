#!/usr/bin/env bash
set -u

PEER_LAN_IP="${1:-}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-12}"

run_bounded() {
  if command -v timeout >/dev/null 2>&1; then
    timeout "${TIMEOUT_SECONDS}s" "$@"
  else
    "$@"
  fi
}

default_interface="$(ip -4 route show default 2>/dev/null | awk 'NR == 1 { print $5 }')"
default_gateway="$(ip -4 route show default 2>/dev/null | awk 'NR == 1 { print $3 }')"
lan_cidr=""
if [[ -n "$default_interface" ]]; then
  lan_cidr="$(ip -o -4 addr show dev "$default_interface" scope global 2>/dev/null | awk 'NR == 1 { print $4 }')"
fi

tailscale_port=""
if command -v tailscale >/dev/null 2>&1; then
  tailscale_port="$(
    run_bounded tailscale debug prefs 2>/dev/null |
      sed -n 's/.*"Port":[[:space:]]*\([0-9][0-9]*\).*/\1/p' |
      head -n 1
  )"
fi

if [[ -z "$tailscale_port" ]] && command -v ss >/dev/null 2>&1; then
  tailscale_port="$(
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

if [[ -z "$tailscale_port" ]]; then
  tailscale_port="41641"
  port_source="default-not-confirmed"
else
  port_source="runtime"
fi

echo "HOSTNAME=$(hostname)"
echo "DEFAULT_INTERFACE=${default_interface:-unknown}"
echo "DEFAULT_GATEWAY=${default_gateway:-unknown}"
echo "LAN_CIDR=${lan_cidr:-unknown}"
echo "TAILSCALE_UDP_PORT=$tailscale_port"
echo "TAILSCALE_PORT_SOURCE=$port_source"
echo

if command -v tailscale >/dev/null 2>&1; then
  echo "=== tailscale status ==="
  run_bounded tailscale status || echo "TAILSCALE_STATUS_TIMEOUT_OR_ERROR"
  echo
  echo "=== tailscale netcheck ==="
  run_bounded tailscale netcheck || echo "TAILSCALE_NETCHECK_TIMEOUT_OR_ERROR"
else
  echo "TAILSCALE_CLI_NOT_FOUND"
fi

echo
echo "=== UDP listeners ==="
if command -v ss >/dev/null 2>&1; then
  ss -lunp 2>/dev/null | grep -E "tailscaled|:${tailscale_port}[[:space:]]" || echo "TAILSCALE_UDP_LISTENER_NOT_VISIBLE"
else
  echo "SS_NOT_FOUND"
fi

echo
echo "=== firewall ==="
if command -v ufw >/dev/null 2>&1; then
  if sudo -n true >/dev/null 2>&1; then
    sudo -n ufw status
  else
    ufw status 2>&1 || echo "UFW_STATUS_REQUIRES_SUDO"
  fi
else
  echo "UFW_NOT_INSTALLED"
fi

if [[ -n "$PEER_LAN_IP" ]]; then
  echo
  echo "=== LAN peer route ==="
  ip route get "$PEER_LAN_IP" 2>&1 || true
  if command -v ping >/dev/null 2>&1; then
    ping -c 2 -W 2 "$PEER_LAN_IP" 2>&1 || true
  fi
fi

