# Source Package Manifest

This archive is the clean v1.4.8 deployment-hardening release source.

## Included

- App, Core, Infrastructure, and Tests projects
- WPF themes and required runtime assets
- pricing and resource-link workbooks
- canonical 302-model release-test inventory
- four maintained PowerShell workflows
- CodeQL workflow
- architecture, security, and company-testing notes

## Excluded

- stale v1.4.7 installer and updater manifest
- Gmail credentials and OAuth tokens
- personal settings and plaintext quote history
- generated customer PDFs
- build output (`bin`, `obj`, `publish`)
- logs, test results, temporary files, nested backups, and repository history
- obsolete patch scripts and release-candidate debris

## Required validation

The Windows release gate must build the installer, run all automated checks, execute the 302-model Gmail create-confirm-delete test, and pass CodeQL before publication.
