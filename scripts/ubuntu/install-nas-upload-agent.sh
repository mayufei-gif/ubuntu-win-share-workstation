#!/usr/bin/env bash
set -euo pipefail

SHARE_ROOT="${SHARE_ROOT:-$HOME/C/ubuntu-win}"
STATE_ROOT="${STATE_ROOT:-$HOME/.local/share/ubuntu-win-share-agent}"
ALLOWED_HOSTS="${ALLOWED_HOSTS:-}"
POLL_SECONDS="${POLL_SECONDS:-15}"
SOURCE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VENV="$STATE_ROOT/venv"
AGENT="$STATE_ROOT/nas_upload_agent.py"
CONFIG="$STATE_ROOT/config.json"
WATCHDOG="$STATE_ROOT/agent-watchdog.sh"
UNIT_DIR="$HOME/.config/systemd/user"
UNIT_PATH="$UNIT_DIR/ubuntu-win-nas-upload-agent.service"
CRON_MARKER="# ubuntu-win-nas-upload-agent"

mkdir -p "$STATE_ROOT" "$UNIT_DIR"
chmod 0700 "$STATE_ROOT"

if [[ -z "${ALLOWED_HOSTS//[[:space:],]/}" ]]; then
  echo "ALLOWED_HOSTS is required. Set it to the NAS IP, DNS name, or SSH alias." >&2
  exit 2
fi

if [[ ! -x "$VENV/bin/python" ]]; then
  python3 -m venv "$VENV"
fi

"$VENV/bin/python" -m pip install --disable-pip-version-check --upgrade pip
"$VENV/bin/python" -m pip install \
  --disable-pip-version-check \
  -r "$SOURCE_DIR/requirements-nas-upload-agent.txt"

install -m 0700 "$SOURCE_DIR/nas_upload_agent.py" "$AGENT"

allowed_args=()
IFS=',' read -r -a hosts <<<"$ALLOWED_HOSTS"
for host in "${hosts[@]}"; do
  host="${host//[[:space:]]/}"
  if [[ -n "$host" ]]; then
    allowed_args+=(--allowed-host "$host")
  fi
done

"$VENV/bin/python" "$AGENT" \
  --init \
  --share-root "$SHARE_ROOT" \
  --state-root "$STATE_ROOT" \
  --poll-seconds "$POLL_SECONDS" \
  "${allowed_args[@]}"

cat >"$WATCHDOG" <<EOF
#!/usr/bin/env bash
set -u
while true; do
  "$VENV/bin/python" "$AGENT" --serve --config "$CONFIG"
  sleep 60
done
EOF
chmod 0700 "$WATCHDOG"

cat >"$UNIT_PATH" <<EOF
[Unit]
Description=Ubuntu Win Share NAS upload agent
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
ExecStart=$VENV/bin/python $AGENT --serve --config $CONFIG
Restart=on-failure
RestartSec=15
NoNewPrivileges=true
PrivateTmp=true

[Install]
WantedBy=default.target
EOF

systemd_status="unavailable"
if command -v timeout >/dev/null 2>&1; then
  if timeout 15s systemctl --user daemon-reload >/dev/null 2>&1 &&
     timeout 20s systemctl --user enable --now ubuntu-win-nas-upload-agent.service >/dev/null 2>&1 &&
     timeout 20s systemctl --user restart ubuntu-win-nas-upload-agent.service >/dev/null 2>&1; then
    systemd_status="enabled"
  fi
else
  if systemctl --user daemon-reload >/dev/null 2>&1 &&
     systemctl --user enable --now ubuntu-win-nas-upload-agent.service >/dev/null 2>&1 &&
     systemctl --user restart ubuntu-win-nas-upload-agent.service >/dev/null 2>&1; then
    systemd_status="enabled"
  fi
fi

linger="$(loginctl show-user "$(id -un)" -p Linger --value 2>/dev/null || true)"
if command -v crontab >/dev/null 2>&1; then
  existing="$(crontab -l 2>/dev/null || true)"
  filtered="$(printf '%s\n' "$existing" | grep -Fv "$CRON_MARKER" || true)"
  if [[ "$linger" == "yes" ]]; then
    printf '%s\n' "$filtered" | crontab -
  else
    {
      printf '%s\n' "$filtered"
      printf '@reboot sleep 20 && %q >/dev/null 2>&1 %s\n' \
        "$WATCHDOG" "$CRON_MARKER"
    } | sed '/^[[:space:]]*$/N;/^\n$/D' | crontab -
  fi
fi

"$VENV/bin/python" "$AGENT" --self-test

echo "NAS_UPLOAD_AGENT_INSTALL_OK share_root=$SHARE_ROOT state_root=$STATE_ROOT linger=${linger:-unknown} systemd=$systemd_status"
if [[ "$linger" != "yes" ]]; then
  echo "INFO: user-systemd linger is disabled; the @reboot cron fallback was installed when crontab was available."
  echo "OPTIONAL_ADMIN_HARDENING: sudo loginctl enable-linger $(id -un)"
fi
