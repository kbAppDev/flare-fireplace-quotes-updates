# Flare Fireplace Quotes v1.4.8 Deployment-Hardened Release Source

Clean Windows WPF release source prepared from the validated v1.4.7 production baseline.

## v1.4.8 hardening

- Quote recall history is encrypted with Windows DPAPI CurrentUser protection.
- Existing plaintext `recent_quotes.json` is migrated automatically and removed after verified encryption.
- App-owned quote PDFs are deleted immediately after successful Gmail draft creation.
- Abandoned temporary PDFs are cleaned after two hours instead of 24 hours.
- Update manifests are limited in size and accepted only from the fixed Flare GitHub release lane.
- Installer URLs must match the exact release version and asset name.
- Installer size and SHA-256 are required and verified before launch.
- Redirects outside approved GitHub asset hosts are rejected.
- Automated tests cover encrypted history migration and updater trust-policy validation.

Windows Authenticode publisher signing is intentionally not required for this internal release lane.
The updater still verifies the exact trusted repository path, expected file size, and SHA-256.

## Included

- Complete App, Core, Infrastructure, and Tests source
- Required pricing and resource workbooks
- Runtime image, icon, font, and theme assets
- Canonical 302-model Gmail integration-test inventory
- Four maintained PowerShell workflows
- GitHub CodeQL workflow
- Architecture, security, and company testing documentation

## Maintained PowerShell workflows

```text
Build_And_Run_Safe.ps1
Run-Final-Release-Gate.ps1
Build_Release_Installer.ps1
Build_Publish_Professional_Release.ps1
```

- `Build_And_Run_Safe.ps1`: build and launch the app for development.
- `Run-Final-Release-Gate.ps1`: format, warning-free builds, tests, vulnerability scans, security scans, and the live Gmail model sweep.
- `Build_Release_Installer.ps1`: build a local self-contained installer and version-specific update manifest.
- `Build_Publish_Professional_Release.ps1`: build and publish the GitHub release after source and CodeQL approval.

## Runtime data

Runtime data is stored under:

```text
%LOCALAPPDATA%\Flare Fireplace Quotes
```

Encrypted quote history:

```text
%LOCALAPPDATA%\Flare Fireplace Quotes
ecent_quotes.json.dpapi
```

Gmail credentials remain restricted to:

```text
%LOCALAPPDATA%\Flare Fireplace Quotes\Credentials\gmail_credentials.json
```

Credentials, OAuth tokens, personal settings, logs, generated PDFs, build output, repository history, and temporary files are excluded from this source package.

## Release status

The Windows release gate passed on July 14, 2026. The publishing workflow still requires CodeQL to pass on the committed GitHub source before v1.4.8 can be published.
