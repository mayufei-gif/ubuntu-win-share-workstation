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
5. Merge through pull request.
6. Tag the release:

   ```bash
   git tag v0.1.0
   git push origin v0.1.0
   ```

7. Create a GitHub release from the tag.
