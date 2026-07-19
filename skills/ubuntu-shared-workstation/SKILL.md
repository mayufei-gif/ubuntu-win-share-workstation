# Ubuntu Shared Workstation Skill

Use this skill when a task needs to inspect or edit Ubuntu-side project files
through a Windows SMB mapped drive.

## Entry Points

- Windows mapped drive: usually `I:\`
- Windows UNC path: `\\<ubuntu-tailnet-ip>\ubuntu-win`
- Ubuntu share path: usually `/home/<user>/C/ubuntu-win`
- Windows 7 on the same NAS: use the Ubuntu LAN/virtual-switch address and the
  `scripts/windows7` compatibility package.

Prefer the UNC path when the drive letter is missing in the current Windows
session.

## Rules

- Treat the mapped drive as the live Ubuntu-backed share.
- Verify with a probe file and SHA-256 when correctness matters.
- Do not assume offline sync. If the network is down, the share is unavailable.
- Do not store Samba passwords, sudo passwords, SSH private keys, or GitHub
  tokens in the shared directory.
- Do not enable SMB1 or NTLMv1 for Windows 7.
- Use SSH for commands that must run on Ubuntu.
- Do not move original project directories just to expose them. Prefer stable
  entry folders, symlinks, generated indexes, or explicit source copies.

## Quick Verification

```powershell
.\scripts\windows\Test-UbuntuWinShareSync.ps1 -DriveLetter I -SshHost ubuntu-vm -RemotePath "/home/mana/C/ubuntu-win"
```

Success requires `SYNC_OK` and matching hashes.
