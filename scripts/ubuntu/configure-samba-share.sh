#!/usr/bin/env bash
set -euo pipefail

SHARE_NAME="${SHARE_NAME:-ubuntu-win}"
SMB_USER="${SMB_USER:-${SUDO_USER:-$USER}}"
OWNER_USER="${SUDO_USER:-$USER}"
OWNER_HOME="$(getent passwd "$OWNER_USER" | cut -d: -f6)"
SHARE_PATH="${SHARE_PATH:-$OWNER_HOME/C/$SHARE_NAME}"
SMB_CONF="/etc/samba/smb.conf"
BACKUP_PATH="/etc/samba/smb.conf.bak.$(date +%Y%m%d_%H%M%S)"

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Run with sudo: sudo bash scripts/ubuntu/configure-samba-share.sh" >&2
  exit 1
fi

if ! command -v smbd >/dev/null 2>&1; then
  apt-get update
  DEBIAN_FRONTEND=noninteractive apt-get install -y samba
fi

mkdir -p "$SHARE_PATH"
chown -R "$OWNER_USER:$OWNER_USER" "$SHARE_PATH"
chmod 0775 "$SHARE_PATH"

cp "$SMB_CONF" "$BACKUP_PATH"

python3 - "$SMB_CONF" "$SHARE_NAME" "$SHARE_PATH" "$SMB_USER" <<'PY'
from pathlib import Path
import sys

conf_path = Path(sys.argv[1])
share_name = sys.argv[2]
share_path = sys.argv[3]
smb_user = sys.argv[4]

text = conf_path.read_text(encoding="utf-8", errors="ignore")
lines = text.splitlines()
out = []
inside_target = False
for line in lines:
    stripped = line.strip()
    if stripped.startswith("[") and stripped.endswith("]"):
        inside_target = stripped.lower() == f"[{share_name.lower()}]"
        if inside_target:
            continue
    if not inside_target:
        out.append(line)

if out and out[-1].strip():
    out.append("")

out.extend([
    f"[{share_name}]",
    f"   path = {share_path}",
    "   browseable = yes",
    "   writable = yes",
    "   read only = no",
    "   guest ok = no",
    f"   valid users = {smb_user}",
    "   create mask = 0664",
    "   directory mask = 0775",
    "   force user = " + smb_user,
    "",
])

conf_path.write_text("\n".join(out), encoding="utf-8")
PY

if ! pdbedit -L | cut -d: -f1 | grep -Fxq "$SMB_USER"; then
  echo "Create Samba password for user '$SMB_USER'. This password is not stored by this script."
  smbpasswd -a "$SMB_USER"
fi

testparm -s >/dev/null
systemctl enable --now smbd
systemctl restart smbd

echo "SMB_SHARE_OK name=$SHARE_NAME path=$SHARE_PATH user=$SMB_USER backup=$BACKUP_PATH"
