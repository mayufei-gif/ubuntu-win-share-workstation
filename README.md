# Ubuntu Win Share Workstation

Customer-ready scripts and runbooks for exposing an Ubuntu workspace over SMB,
mapping it on Windows, and verifying that Windows edits are visible on Ubuntu.

This project is intentionally small. It does not copy private project data into
Git. It version-controls the infrastructure needed to recreate and validate the
share.

## What This Solves

- Ubuntu exports a user-owned workspace directory through Samba.
- Windows maps that share to a drive letter, for example `I:`.
- Edits made through the Windows drive are written to the Ubuntu-backed share.
- Other agents or developers can use the mapped drive as the shared project
  entry point.
- GitHub Desktop users can collaborate through normal branches, commits, pull
  requests, and releases.

Important: this is not an offline sync engine. The Windows drive is an SMB
network mount. When the network or tailnet is offline, writes should fail
instead of being queued locally.

## Quick Start

On Ubuntu:

```bash
sudo bash scripts/ubuntu/configure-samba-share.sh
```

On Windows PowerShell:

```powershell
.\scripts\windows\Map-UbuntuWinShare.ps1 -Share "\\100.x.y.z\ubuntu-win" -DriveLetter I
.\scripts\windows\Test-UbuntuWinShareSync.ps1 -DriveLetter I -SshHost ubuntu-vm -RemotePath "/home/mana/C/ubuntu-win"
```

Replace `100.x.y.z` with the Ubuntu machine tailnet IP or DNS name.

## Repository Layout

```text
scripts/
  ubuntu/
    configure-samba-share.sh     Configure the Ubuntu Samba share.
    verify-share-path.sh         Verify the Ubuntu-side shared directory.
  windows/
    Map-UbuntuWinShare.ps1       Map the SMB share on Windows.
    Register-UbuntuWinShareTask.ps1
                                  Optional hidden logon remount task.
    Test-UbuntuWinShareSync.ps1  Windows-to-Ubuntu sync proof.
skills/
  ubuntu-shared-workstation/
    SKILL.md                     Agent instruction card for shared folders.
docs/
  github-desktop-workflow.md     Multi-user workflow.
  release-process.md             Versioning and release rules.
  sync-verification.md           Verification model and commands.
```

## Safe Defaults

- No passwords, tokens, private keys, runtime logs, databases, or project
  payloads are committed.
- Local machine addresses are passed as parameters, not hard-coded.
- The Windows scheduled task is hidden and logon-only by default. It does not
  create a high-frequency flashing PowerShell loop.
- The Ubuntu script backs up `smb.conf` before editing.

## Versioning

Use semantic versions:

- Patch: docs, validation, or bug fix.
- Minor: new script option or workflow.
- Major: changed defaults or breaking behavior.

See [docs/release-process.md](docs/release-process.md).
