# Security and Release Notes

## Sensitive files excluded from this source package

- `LocalData/gmail_credentials.json`
- OAuth token files
- generated quote PDFs
- debug logs
- build output folders
- audit ZIPs

## Implemented hardening

- Gmail draft flow uses reduced Gmail scopes.
- Gmail OAuth token storage uses Windows DPAPI-protected file storage.
- Gmail credentials are consumed only from `%LOCALAPPDATA%\Flare Fireplace Quotes\Credentials\gmail_credentials.json` after a one-time import from an explicitly configured file or the installed LocalData file.
- Email headers are sanitized.
- Recipient and BCC values are validated.
- User-facing error messages are redacted where practical.
- Temporary quote PDFs are stored under app-owned AppData paths and are intended to auto-clean.
- Update package hash verification support is preserved.

## Team testing cautions

- Do not share generated PDFs containing customer information outside approved channels.
- Do not email or upload `gmail_credentials.json` to chat, tickets, or public storage.
- Do not commit `bin`, `obj`, logs, tokens, credentials, or generated PDFs.
- Close the app before rebuilding to avoid locked DLL errors.

## Known operational fallback

If Live Preview does not render on a tester's machine, use Open Generated PDF. Live Preview must not block quote creation or Gmail draft creation.
