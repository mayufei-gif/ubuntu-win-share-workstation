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
WATCHDOG_PID="$STATE_ROOT/agent-watchdog.pid"
WATCHDOG_LOCK="$STATE_ROOT/agent-watchdog.lock"
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
set -euo pipefail
exec 9>"$WATCHDOG_LOCK"
flock -n 9 || exit 0
printf '%s\n' "\$\$" >"$WATCHDOG_PID"
trap 'rm -f "$WATCHDOG_PID"' EXIT
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

run_user_systemctl() {
  if command -v timeout >/dev/null 2>&1; then
    timeout 20s systemctl --user "$@"
  else
    systemctl --user "$@"
  fi
}

remove_cron_entry() {
  if ! command -v crontab >/dev/null 2>&1; then
    return
  fi
  existing="$(crontab -l 2>/dev/null || true)"
  printf '%s\n' "$existing" |
    grep -Fv "$CRON_MARKER" |
    crontab - || true
}

install_cron_entry() {
  if ! command -v crontab >/dev/null 2>&1; then
    echo "crontab is required when user-systemd linger is unavailable." >&2
    return 1
  fi
  existing="$(crontab -l 2>/dev/null || true)"
  filtered="$(printf '%s\n' "$existing" | grep -Fv "$CRON_MARKER" || true)"
  {
    printf '%s\n' "$filtered"
    printf '@reboot sleep 20 && %q >/dev/null 2>&1 %s\n' \
      "$WATCHDOG" "$CRON_MARKER"
  } | sed '/^[[:space:]]*$/N;/^\n$/D' | crontab -
}

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

count_exact_processes() {
  local expected="$1"
  local count=0
  local pid
  local args
  while read -r pid args; do
    if [[ -n "$pid" &&
          "$pid" != "$$" &&
          "$args" == "$expected" ]]; then
      count=$((count + 1))
    fi
  done < <(ps -u "$(id -u)" -o pid=,args=)
  printf '%s\n' "$count"
}

if ! command -v flock >/dev/null 2>&1; then
  echo "flock is required for the single-instance cron watchdog." >&2
  exit 2
fi

linger="$(loginctl show-user "$(id -un)" -p Linger --value 2>/dev/null || true)"
remove_cron_entry
stop_exact_processes "$WATCHDOG"
run_user_systemctl disable --now \
  ubuntu-win-nas-upload-agent.service >/dev/null 2>&1 || true
stop_exact_processes "$AGENT --serve --config $CONFIG"
rm -f "$WATCHDOG_PID"

systemd_status="disabled"
startup_mode="none"
if [[ "$linger" == "yes" ]] &&
   run_user_systemctl daemon-reload >/dev/null 2>&1 &&
   run_user_systemctl enable --now \
     ubuntu-win-nas-upload-agent.service >/dev/null 2>&1 &&
   run_user_systemctl restart \
     ubuntu-win-nas-upload-agent.service >/dev/null 2>&1 &&
   run_user_systemctl is-active --quiet \
     ubuntu-win-nas-upload-agent.service; then
  systemd_status="enabled"
  startup_mode="systemd"
else
  run_user_systemctl disable --now \
    ubuntu-win-nas-upload-agent.service >/dev/null 2>&1 || true
  install_cron_entry
  nohup "$WATCHDOG" >/dev/null 2>&1 &
  startup_mode="cron"
fi

watchdog_count=0
agent_count=0
for _ in $(seq 1 30); do
  watchdog_count="$(
    count_exact_processes "bash $WATCHDOG"
  )"
  agent_count="$(
    count_exact_processes "$VENV/bin/python $AGENT --serve --config $CONFIG"
  )"
  if [[ "$startup_mode" == "systemd" &&
        "$agent_count" == "1" ]]; then
    break
  fi
  if [[ "$startup_mode" == "cron" &&
        "$watchdog_count" == "1" &&
        "$agent_count" == "1" ]]; then
    break
  fi
  sleep 1
done

if [[ "$startup_mode" == "systemd" &&
      "$agent_count" != "1" ]]; then
  echo "The systemd upload agent did not reach a single-instance state." >&2
  exit 3
fi
if [[ "$startup_mode" == "cron" &&
      ( "$watchdog_count" != "1" || "$agent_count" != "1" ) ]]; then
  echo "The cron upload agent did not reach a single-instance state." >&2
  exit 3
fi

"$VENV/bin/python" "$AGENT" --self-test

echo "NAS_UPLOAD_AGENT_INSTALL_OK share_root=$SHARE_ROOT state_root=$STATE_ROOT linger=${linger:-unknown} systemd=$systemd_status startup=$startup_mode watchdogs=$watchdog_count agents=$agent_count"
if [[ "$startup_mode" == "cron" ]]; then
  echo "INFO: exactly one cron watchdog startup entry is active; the user service is disabled."
fi
if [[ "$linger" != "yes" ]]; then
  echo "OPTIONAL_ADMIN_HARDENING: sudo loginctl enable-linger $(id -un)"
fi
