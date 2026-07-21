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
- Windows 7 clients on the same NAS or LAN can use a compatibility package
  based on built-in `cmd.exe` tools and the last Win7-compatible Git for
  Windows release.

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

On Windows 7, use the Ubuntu VM LAN address instead of requiring a current
Tailscale client:

```bat
scripts\windows7\install-client.cmd \\192.168.x.x\ubuntu-win I: mana mana@192.168.x.x /home/mana/C/ubuntu-win 10
scripts\windows7\test-share-sync.cmd
```

Run `install-client.cmd` from an elevated Command Prompt when logon and
self-heal tasks are required.

Current GitHub Desktop releases do not support Windows 7. The Win7 package uses
Git for Windows plus `git gui` and repository helper commands to provide the
same branch, commit, pull, push, and review workflow. See
[docs/windows7-compatibility.md](docs/windows7-compatibility.md).

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
    Test-Windows7SameNasE2E.ps1  Non-installing two-round three-end verifier.
  windows7/
    install-client.cmd           Create local configuration and register tasks.
    map-share.cmd                Interactive first-time SMB mapping.
    ensure-share.cmd             Silent idempotent remount.
    register-tasks.cmd           Hidden logon and low-frequency self-heal.
    test-share-sync.cmd          Win7-compatible SHA-256 and SSH proof.
    git-workflow.cmd             GitHub Desktop equivalent workflow.
    win7-readiness.cmd           Win7 SP1 and dependency readiness check.
    acceptance-test.cmd          End-to-end Win7 acceptance gate.
    collect-diagnostics.cmd      Sanitized support report.
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
- The Windows 7 self-heal task runs through hidden `wscript.exe` and defaults
  to a 10-minute interval.
- The Ubuntu script backs up `smb.conf` before editing.

## Versioning

Use semantic versions:

- Patch: docs, validation, or bug fix.
- Minor: new script option or workflow.
- Major: changed defaults or breaking behavior.

See [docs/release-process.md](docs/release-process.md).

## Windows 7 Release Candidate

The current customer-test candidate is
`UbuntuWinShare-Win7-0.3.2-Setup.exe`. Double-click it, select the local
folder, and enter the Samba and NAS account settings. The native x86 Setup
checks .NET Framework 3.5.1, the Workstation service, and `robocopy.exe`
before starting the managed client for the current user. It starts with
Windows, retries quietly after network recovery, and prompts once before
enabling Ubuntu-to-NAS uploads.

Normal installation is gated to Windows 7 SP1. On Windows 8, 10, or 11, the
Setup exits without installing; non-destructive build and integration self-test
modes remain available to maintainers.

The customer Setup self-test is:

```bat
UbuntuWinShare-Win7-0.3.2-Setup.exe --self-test --result setup-self-test.txt
```

It does not install, map a drive, start the tray client, or request elevation.

The same customer package includes
`UbuntuWinShare-Win7-0.3.2-Uninstall.exe` for a separate double-click
uninstall. It removes only the Win7-side client state and leaves the original
Windows folder, Ubuntu share data, and NAS data intact.

If .NET Framework 3.5.1 or the Workstation service cannot be repaired by
Setup, run `人工兜底\安装_管理员.cmd` from the same customer package. It repairs
only those system prerequisites, then launches the contained client.

The package also includes `Windows7_实机验收清单.txt`. Complete every item on
an actual Windows 7 SP1 computer before promoting this release candidate.

A concise Chinese installation guide is included at
`docs/Windows7_客户安装说明.txt`.

After the first successful NAS upload, the Ubuntu agent has established SSH-key
access and the application removes the saved NAS password from its local DPAPI
primary and backup configuration files. Changing the temporary NAS password
after that does not stop future key-based uploads. Changing the source folder,
share, profile, or NAS destination resets the applicable synchronization and
upload confirmation state before the new settings are used.

Upload jobs are written as unique files in the shared queue. This avoids
same-name replacement locks on Samba and lets the Ubuntu agent retry a job
after a temporary NAS or network failure.

The legacy command-line ZIP can also be transferred to a Win7 machine before
Git is installed. Run:

```bat
scripts\windows7\win7-readiness.cmd
scripts\windows7\install-client.cmd \\192.168.x.x\ubuntu-win I: mana mana@192.168.x.x /home/mana/C/ubuntu-win 10
scripts\windows7\acceptance-test.cmd
```

Maintainers can verify the same executable without installing it on the build
machine:

```powershell
.\scripts\windows\Test-Windows7SameNasE2E.ps1 `
  -ClientExe .\artifacts\UbuntuWinShare-Win7-0.3.2.exe `
  -Share "\\<Ubuntu-IP>\ubuntu-win" `
  -NasHost "<NAS-IP-or-DNS>" `
  -NasUser "<NAS SSH user>"
```

Current Tailscale for Windows releases do not run on Windows 7. A remote Win7
client must therefore reach the Ubuntu Samba address through a supported
Tailscale subnet router or gateway, or use an Ubuntu LAN address that is
directly routable from that client. When the supplied tailnet route reaches
this Ubuntu VM, enter `\\100.95.140.72\ubuntu-win` as the share UNC. The
installer accepts either reachable UNC form.

Do not tag or publish `v0.3.2` until the real Windows 7 SP1 acceptance
checklist passes.
