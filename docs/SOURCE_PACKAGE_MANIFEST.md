# Source Package Manifest

This archive contains the clean, publish-ready v1.4.9 email-recipient hotfix source that passed the complete Windows release gate and the reported FF60H/new-recipient reproduction test.

## Included

- App, Core, Infrastructure, and Tests projects
- recipient normalization and Gmail MIME regression tests
- WPF themes and required runtime assets
- pricing and resource-link workbooks
- canonical 302-model release-test inventory
- four maintained PowerShell workflows
- one release-package publishing launcher
- CodeQL workflow
- architecture, security, and company-testing notes

## Excluded

- installers and updater manifests before publication
- Gmail credentials and OAuth tokens
- personal settings and quote history
- generated customer PDFs
- build output (`bin`, `obj`, `publish`)
- logs, test results, temporary files, nested backups, and repository history

## Required publication validation

The publishing launcher must push the exact clean source, require the matching GitHub CodeQL run to pass, publish the two exact updater assets, verify the live v1.4.9 manifest, build the final Desktop backup, and update the local installation.
