# Security and Release Notes

## Sensitive files excluded

- `LocalData/gmail_credentials.json`
- OAuth token files
- personal settings
- generated quote PDFs
- debug logs
- build output and repository history

## Implemented hardening

- Gmail OAuth tokens use Windows DPAPI CurrentUser protection.
- Recall quote history uses DPAPI CurrentUser encryption in `recent_quotes.json.dpapi`.
- Existing plaintext recall history is migrated once, verified, overwritten, and deleted.
- Gmail credentials are consumed only from `%LOCALAPPDATA%\Flare Fireplace Quotes\Credentials\gmail_credentials.json` after an explicit one-time import.
- Email headers are sanitized and recipients are validated.
- Logs and user-facing errors redact local paths, email addresses, and sensitive values where practical.
- Quote PDFs are created only under the app-owned Temp directory.
- The generated PDF is deleted immediately after a Gmail draft is confirmed.
- Failed or abandoned temporary PDFs are retained briefly for retry and cleaned after two hours.
- Update checks are pinned to the Flare-managed GitHub repository and exact release asset naming.
- Update manifests must provide a valid semantic version, 64-character SHA-256, and expected installer size.
- Installer downloads are rejected when the host, path, size, or SHA-256 does not match the trusted manifest data.

## Intentional signing decision

Windows Authenticode publisher signing is not required for this internal deployment lane. Windows may therefore show an unknown-publisher warning on a new machine. This does not disable the app's repository, size, and SHA-256 update verification.

## Team cautions

- Distribute installers only through the approved GitHub release/update lane.
- Do not email or upload `gmail_credentials.json` to chat, tickets, or public storage.
- Do not commit `bin`, `obj`, logs, tokens, credentials, or generated PDFs.
- Close the app before rebuilding to avoid locked DLL errors.
