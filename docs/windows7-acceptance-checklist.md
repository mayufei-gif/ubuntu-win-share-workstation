# Windows 7 Acceptance Checklist

The project is fully accepted on a Win7 client only when all rows pass on the
actual machine.

| Requirement | Command or evidence | Required result |
| --- | --- | --- |
| OS | `win7-readiness.cmd` | Windows 7 version `6.1`, SP1 |
| SMB client | `sc query lanmanworkstation` | `RUNNING` |
| Network route | Open the Ubuntu UNC before install | Win7 reaches Ubuntu by LAN or a supported subnet router/gateway |
| Share mapping | `net use I:` | Correct Ubuntu UNC, status OK |
| Double-click install | Run `UbuntuWinShare-Win7-0.3.2.exe` | Setup completes only on Win7 SP1 |
| Write path | Edit a nested local test file | Equal local and Ubuntu SHA-256 |
| Logon startup | Inspect `HKCU\...\Run\UbuntuWinShare` after reboot | Tray client starts once |
| Network recovery | Disconnect and reconnect LAN, then edit a test file | Sync resumes without manual remap |
| No flashing | Observe startup, offline retries, and recovery | No console or PowerShell window |
| First NAS prompt | Complete first Windows-to-Ubuntu sync | Confirmation dialog appears once |
| NAS upload | Click OK in the prompt | Ubuntu result is `success`, NAS hash matches |
| Password rotation | Change the temporary NAS password, then edit again | Later upload succeeds by SSH key |
| Git client | `git --version` | Win7-compatible Git runs |
| GitHub access | `git ls-remote` | Remote HEAD returned |
| Double-click uninstall | Run the separate uninstaller after a completed sync | Local client state is removed; source, Ubuntu, and NAS files remain |
| Legacy CLI gate | `acceptance-test.cmd` when command scripts are used | `WIN7_ACCEPTANCE_OK` |

Attach the output from `collect-diagnostics.cmd` when reporting a failure.
