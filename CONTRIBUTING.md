# Contributing

Use small branches and keep environment-specific data out of Git.

## Workflow

1. Pull the latest `main`.
2. Create a branch named `feature/<topic>`, `fix/<topic>`, or `docs/<topic>`.
3. Make focused changes.
4. Run local validation:

   ```powershell
   pwsh -NoProfile -File scripts/windows/Test-PowerShellSyntax.ps1
   ```

   ```bash
   bash -n scripts/ubuntu/*.sh
   ```

5. Commit with a clear message.
6. Push and open a pull request.

## Rules

- Do not commit passwords, SSH keys, tokens, private IP inventories, logs, or
  customer data.
- Do not commit generated mapped-drive content.
- Do not add high-frequency scheduled tasks by default.
- Prefer parameters and examples over machine-specific constants.
- Keep scripts idempotent so they can be rerun safely.
