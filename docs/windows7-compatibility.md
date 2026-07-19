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

## Security Position

Windows 7 and its compatible Git/Tailscale clients are no longer receiving
normal platform support. Keep this client isolated on the NAS/LAN, restrict
the Samba user to the required share, and do not expose SMB port 445 to the
public internet.
