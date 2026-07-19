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
   .\scripts\windows\Build-Windows7Release.ps1 -Version 0.3.1
   ```

6. Verify the executable self-test marker and both SHA-256 files under
   `artifacts/`.
7. Merge through pull request.
8. Tag the release:

   ```bash
   git tag v0.3.1
   git push origin v0.3.1
   ```

9. Create a GitHub release from the tag and attach:

   - `UbuntuWinShare-Win7-0.3.1.exe`
   - `UbuntuWinShare-Win7-0.3.1.exe.sha256`
   - `UbuntuWinShare-Win7-0.3.1-customer.zip`
   - `UbuntuWinShare-Win7-0.3.1-customer.zip.sha256`
