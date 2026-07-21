# Changelog

## 0.3.2 - Unreleased release candidate

- Added an allowlisted NAS LAN fallback route for Ubuntu uploads when the
  primary Tailscale NAS route has a transient transport failure.
  Authentication rejection and host-key mismatch remain terminal failures.

- Added a native x86 bootstrapper so a Win7 SP1 machine with .NET Framework
  3.5.1 disabled can repair prerequisites before the managed client starts.
- Added a double-click administrator fallback for the same dependency repair,
  plus non-destructive self-tests for the bootstrapper and fallback script.
- Made the customer ZIP use `UbuntuWinShare-Win7-0.3.2-Setup.exe` as its
  primary entry point and package the fallback client only under `人工兜底`.
- Added a separate double-click Windows 7 uninstaller executable.
- Embedded the uninstaller in the installer so tray and Control Panel removal
  use the same cleanup path.
- Added a named exit signal so the running tray client shuts down before its
  installed files are removed.
- Added a path-scoped Win7 process-tree fallback for removing older installed
  clients without leaving their `robocopy` child process running.
- Limited uninstall cleanup to local client state, the configured matching
  drive mapping, and the two legacy Win7 recovery tasks.
- Added non-destructive self-tests for delete-path and drive-mapping safety.
- Added a Windows 7 SP1 runtime gate so the customer installer cannot
  accidentally install on Windows 8, 10, or 11 build machines.
- Replaced unbounded synchronization with a supervised worker that terminates
  its `robocopy` process tree after a five-minute timeout.
- Added atomic DPAPI configuration writes, a readable backup from the first
  save, and automatic recovery from a corrupt primary configuration.
- Made the uninstaller read the backup configuration before deciding whether
  a mapped drive belongs to this application.
- Ensured the Ubuntu NAS upload agent has exactly one startup owner: user
  systemd when linger is enabled, otherwise a locked cron watchdog.
- Added bounded NAS SSH reconnection for transient Paramiko session failures,
  including `No existing session`, without retrying authentication rejection.
- Made agent installation wait for and verify exactly one active upload agent,
  plus one watchdog when the cron fallback owns startup.
- Added a repeatable two-round Windows-to-Ubuntu-to-NAS SHA-256 E2E verifier.
- Rewrote both DPAPI configuration copies after successful SSH-key upload so
  an older NAS password cannot remain in `config.dat.bak`.
- Reset synchronization, upload authorization, queued-result, and public-key
  trust state when their corresponding source, share, or NAS settings change.
- Prohibited DTD and external entity resolution when reading configuration,
  worker-result, upload-result, and Ubuntu upload-agent public-key XML.
- Added package-level and release-level `SHA256SUMS.txt` manifests.
- Added a customer-facing real Windows 7 SP1 acceptance checklist covering
  install, two-round hashes, password rotation, restart, reconnect, no-flash,
  uninstall, and three-side data retention.

This version must remain untagged until installation, logon startup, network
recovery, no-flash behavior, password rotation, and uninstall retention pass on
a real Windows 7 SP1 machine.

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
