# GitHub Desktop Workflow

This repository is designed for GitHub Desktop users.

Windows 7 cannot run a current GitHub Desktop release. On Windows 7, install
the last compatible Git for Windows release and use:

```bat
scripts\windows7\git-workflow.cmd gui
scripts\windows7\git-workflow.cmd pull
scripts\windows7\git-workflow.cmd branch fix/example
scripts\windows7\git-workflow.cmd publish
```

## First Setup

1. Open GitHub Desktop.
2. Select `File` -> `Clone repository`.
3. Clone this repository.
4. Keep local configuration in `.local/` or pass settings through script
   parameters.

## Daily Work

1. Click `Fetch origin`.
2. Click `Pull origin` when updates exist.
3. Create a new branch before editing.
4. Commit small, related changes.
5. Push the branch.
6. Open a pull request.

## Branch Naming

- `fix/<short-name>` for bug fixes.
- `feature/<short-name>` for new behavior.
- `docs/<short-name>` for documentation.
- `ops/<short-name>` for deployment or task changes.

## Conflict Rules

- Prefer keeping script defaults generic.
- Put machine-specific values in local files ignored by Git.
- If two users edit the same script, resolve by keeping the more idempotent and
  safer behavior, then rerun validation.

## What Must Not Be Committed

- Passwords or Samba user passwords.
- SSH private keys.
- GitHub tokens.
- Windows Credential Manager exports.
- Customer project files copied from a mapped drive.
- Runtime logs, databases, browser profiles, or cache folders.
