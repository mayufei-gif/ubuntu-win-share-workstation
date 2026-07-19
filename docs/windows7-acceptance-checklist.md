# Windows 7 Acceptance Checklist

The project is fully accepted on a Win7 client only when all rows pass on the
actual machine.

| Requirement | Command or evidence | Required result |
| --- | --- | --- |
| OS | `win7-readiness.cmd` | Windows 7 version `6.1`, SP1 |
| SMB client | `sc query lanmanworkstation` | `RUNNING` |
| Share mapping | `net use I:` | Correct Ubuntu UNC, status OK |
| Write path | `test-share-sync.cmd` | `SYNC_OK`, equal SHA-256 |
| Logon recovery | `schtasks /Query` | Win7 Logon task exists |
| Network recovery | `schtasks /Query` | Win7 Self Heal task exists |
| No flashing | Observe both task runs | No console window |
| Git client | `git --version` | Win7-compatible Git runs |
| GitHub access | `git ls-remote` | Remote HEAD returned |
| Final gate | `acceptance-test.cmd` | `WIN7_ACCEPTANCE_OK` |

Attach the output from `collect-diagnostics.cmd` when reporting a failure.
