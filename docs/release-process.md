# Release Process

Use semantic versioning.

## Version Types

- `MAJOR`: changed defaults or incompatible behavior.
- `MINOR`: new script option, new platform support, or new workflow.
- `PATCH`: bug fix, docs update, validation improvement, or safer defaults.

## Release Checklist

1. Confirm `CHANGELOG.md` has the new version entry.
2. Run PowerShell syntax validation.
3. Run Bash syntax validation.
4. Test sync verification on at least one real mapped share when behavior
   changes.
5. Build the Windows 7 customer package:

   ```powershell
   .\scripts\windows\Build-Windows7Release.ps1 -Version 0.3.2
   ```

6. Verify the executable self-test marker and both SHA-256 files under
   `artifacts/`.
7. Run `Test-Windows7SameNasE2E.ps1` for two successful rounds and retain the
   JSON evidence.
8. Complete `docs/windows7-acceptance-checklist.md` on a real Windows 7 SP1
   machine. Build-machine tests are not a substitute for this gate.
9. Merge through pull request.
10. Tag the release:

   ```bash
   git tag v0.3.2
   git push origin v0.3.2
   ```

11. Create a GitHub release from the tag and attach:

   - `UbuntuWinShare-Win7-0.3.2.exe`
   - `UbuntuWinShare-Win7-0.3.2.exe.sha256`
   - `UbuntuWinShare-Win7-0.3.2-Uninstall.exe`
   - `UbuntuWinShare-Win7-0.3.2-Uninstall.exe.sha256`
   - `UbuntuWinShare-Win7-0.3.2-customer.zip`
   - `UbuntuWinShare-Win7-0.3.2-customer.zip.sha256`

Until steps 7 and 8 pass, call the output a release candidate and do not create
or move the `v0.3.2` tag.
