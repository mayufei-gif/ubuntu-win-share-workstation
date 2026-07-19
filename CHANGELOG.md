# Changelog

## 0.3.1 - 2026-07-19

- Throttled offline remount attempts so a disconnected share cannot trigger a
  sync attempt every three seconds.
- Made duplicate background startup silent.
- Triggered automatic NAS uploads only when robocopy reports copied files,
  instead of treating destination-only status files as source changes.
- Published each NAS upload job under a unique filename so Samba file locks
  cannot block replacement of an older queued job.
- Isolated setup edits so cancelling the settings window cannot mutate the
  active configuration.
- Removed the locally protected NAS password after the first successful upload
  establishes or confirms SSH-key access.
- Added CI coverage for the Ubuntu NAS upload agent.
- Pinned the tested Ubuntu agent dependency set and added a concise Chinese
  customer installation guide.

## 0.3.0 - 2026-07-19

- Added a single-file Windows 7 tray application with self-install,
  current-user startup, SMB mapping, local-folder watching, and hidden
  `robocopy` synchronization.
- Added an encrypted upload-job queue and an Ubuntu user service that uploads
  changed files to an allowlisted NAS account over SSH.
- Added a one-time confirmation prompt that enables automatic NAS uploads after
  the first successful Windows-to-Ubuntu sync.
- Protected Samba and NAS passwords at rest with Windows DPAPI and encrypted
  NAS credentials in transit with the Ubuntu agent RSA public key.

## 0.2.1 - 2026-07-19

- Added a Windows 7 SP1 readiness checker.
- Added a full Win7 acceptance gate for mapping, tasks, SHA-256, and GitHub.
- Added sanitized diagnostic collection for remote support.
- Added a reproducible Win7 offline bundle builder.

## 0.2.0 - 2026-07-19

- Added Windows 7 SMB mapping and hidden self-heal scripts.
- Added Windows 7 SHA-256 sync verification through Git for Windows SSH.
- Added a Git GUI workflow as the Windows 7 GitHub Desktop replacement.
- Documented same-NAS/LAN mode and legacy Tailscale limitations.
- Explicitly kept Samba at SMB2 or newer with NTLMv2 authentication.

## 0.1.0 - 2026-07-19

- Initial public repository.
- Added Ubuntu Samba configuration script.
- Added Windows SMB drive mapping script.
- Added optional hidden logon remount task.
- Added Windows-to-Ubuntu sync verification script.
- Added GitHub Desktop collaboration and release docs.
