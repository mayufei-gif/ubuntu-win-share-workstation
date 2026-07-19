# Sync Verification

The mapped drive is an SMB mount. Windows writes go directly to the Ubuntu
share path. Verification should prove content identity, not just drive
visibility.

## Required Proof

1. Windows can see the mapped drive or UNC path.
2. Windows writes a unique probe file.
3. Ubuntu sees the same file under the configured share path.
4. SHA-256 matches on both sides.
5. Edited content on Windows is visible on Ubuntu.

## Windows Command

```powershell
.\scripts\windows\Test-UbuntuWinShareSync.ps1 `
  -DriveLetter I `
  -SshHost ubuntu-vm `
  -RemotePath "/home/mana/C/ubuntu-win"
```

## Ubuntu Command

```bash
bash scripts/ubuntu/verify-share-path.sh /home/mana/C/ubuntu-win
```

## Windows 7 Command

```bat
scripts\windows7\test-share-sync.cmd
```

The Win7 script uses built-in `certutil` for SHA-256 and the `ssh.exe` bundled
with Git for Windows for the Ubuntu-side hash.

## Interpreting Results

- Matching hashes mean the share is working.
- A missing drive means Windows has not mounted the SMB share in this user
  session.
- A missing Ubuntu file means the mapped drive points somewhere other than the
  intended Samba path.
- If the tailnet or network is offline, the SMB drive should be treated as
  unavailable. This project does not queue offline writes.
