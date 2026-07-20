# Windows 7 Compatibility

## Supported Design

Windows 7 accesses the same Ubuntu Samba share over the NAS virtual network or
LAN:

```text
Windows 7 VM -> SMB2 -> Ubuntu VM -> shared directory
```

Use the Ubuntu VM LAN address, for example:

```text
\\192.168.x.x\ubuntu-win
```

This avoids requiring a current Tailscale client on Windows 7. A legacy
Tailscale build exists for Win7, but it is end-of-life and is not the default
design for this project.

## Protocol Rules

- Minimum Samba protocol: `SMB2_02`.
- Maximum Samba protocol: `SMB3`.
- Authentication: NTLMv2.
- SMB1 and NTLMv1 must stay disabled.

Windows 7 supports SMB2 and does not require SMB1 for this share.

## Git and GitHub

Current GitHub Desktop releases require a newer Windows version. Use Git for
Windows `2.47.1.2`, the final Win7-compatible release, together with `git gui`
and `scripts/windows7/git-workflow.cmd`.

Prefer an SSH GitHub remote:

```text
git@github.com:mayufei-gif/ubuntu-win-share-workstation.git
```

The Git for Windows package supplies `git.exe`, `git gui`, and `ssh.exe`.

## Installation

For normal users, double-click the release executable:

```text
UbuntuWinShare-Win7-0.3.2.exe
```

The setup window asks for the local source folder, Ubuntu Samba share, Samba
credentials, and NAS SSH account. Configuration is protected with Windows
DPAPI for the current user. After the first successful upload, the client
removes the NAS password because the Ubuntu agent has established SSH-key
access.

The tray application starts through the current-user Run key. Network failures
are retried in the background no more often than the configured interval, with
a 30-second minimum, and do not create PowerShell or console windows.

The customer package also contains
`UbuntuWinShare-Win7-0.3.2-Uninstall.exe`. Double-click it on the Win7 client
to remove only the local application, configuration, startup entry, matching
mapped drive, and legacy recovery tasks. It does not delete the source folder,
Ubuntu share data, or NAS data.

The command-line compatibility scripts remain available for diagnostics and
manual deployments.

## Tailscale network prerequisite

Current Tailscale for Windows releases require Windows 10 or newer. Windows 7
clients cannot be treated as native tailnet nodes. Use one of these supported
network layouts:

- Connect Win7 directly to the Ubuntu Samba service over a trusted LAN.
- Put a supported Tailscale device on the Win7 LAN and route the Ubuntu
  destination through it.
- Expose only the required private SMB route through a managed VPN gateway.

The Win7 setup accepts any reachable UNC address. Do not distribute an
unsupported legacy Tailscale client as part of this package.

Run from an elevated Win7 command prompt for full automatic recovery:

```bat
install-client.cmd \\192.168.x.x\ubuntu-win I: mana mana@192.168.x.x /home/mana/C/ubuntu-win 10
```

Arguments:

1. SMB UNC path.
2. Drive letter.
3. Samba user.
4. SSH target.
5. Ubuntu shared-directory path.
6. Self-heal interval in minutes.

The installer writes `share-config.local.cmd`, which is ignored by Git. It
does not write a password. The first interactive mapping prompts for the Samba
password through `net use`.

A normal command prompt can map and verify the share. Administrator rights are
required to register or remove the logon and self-heal scheduled tasks.

## Recovery Behavior

- A hidden logon task checks the mapping.
- A hidden low-frequency task retries after network recovery.
- The default interval is 10 minutes.
- The task calls `wscript.exe` with window style hidden, so it does not flash a
  console window.

Remove the tasks with:

```bat
unregister-tasks.cmd
```

## Acceptance Gate

After installation, run:

```bat
win7-readiness.cmd
acceptance-test.cmd
```

Completion requires:

- `WIN7_READINESS_OK`
- `SYNC_OK`
- both scheduled tasks visible
- GitHub remote query succeeds
- `WIN7_ACCEPTANCE_OK`

If a gate fails, collect a report:

```bat
collect-diagnostics.cmd
```

The report contains OS, network, task, mapping, Git, and SSH state. It does not
collect passwords or private-key contents.

## Security Position

Windows 7 and its compatible Git clients are no longer receiving normal
platform support. Keep this client isolated on the NAS/LAN, restrict the Samba
user to the required share, and do not expose SMB port 445 to the public
internet.
