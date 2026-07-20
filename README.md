# Flare Fireplace Quotes v1.4.9 Email-Recipient Hotfix

Publish-ready Windows WPF source prepared from the exact v1.4.9 source that passed the complete local release gate and the reported FF60H/new-recipient reproduction test.

## v1.4.9 fix

- Normalizes customer email addresses copied from Outlook, websites, PDFs, and formatted documents.
- Removes invisible Unicode and control characters before Gmail MIME headers are built.
- Converts full-width `＠` and `．` characters to standard `@` and `.` characters.
- Removes `mailto:` wrappers, display-name wrappers, nonbreaking spaces, and trailing punctuation.
- Rejects missing, malformed, or multiple addresses in the single customer-email field before Gmail is called.
- Refreshes the Gmail draft snapshot from the currently visible normalized email, preventing stale recipient data from a prior preview.
- Revalidates the recipient inside the Gmail MIME builder.
- Adds regression tests for the reported `Invalid To header` failure and final decoded MIME `To:` header.

The v1.4.8 deployment protections remain in place: DPAPI-encrypted quote history, temporary-PDF cleanup, pinned GitHub updater paths, installer size verification, and SHA-256 verification.

Windows Authenticode publisher signing remains intentionally optional for this internal deployment lane.

## Validation completed

- Warning-free Release builds passed.
- 20 of 20 unit and regression tests passed.
- Dependency vulnerability and source-security scans passed.
- All 302 canonical fireplace models passed live Gmail create-confirm-delete testing.
- A brand-new auto-filled FF60H quote reproduced from the reported failure successfully created its Gmail draft.

## Publish and install

Run `PUBLISH_v1.4.9.ps1` from this package. It will:

1. Push the exact clean v1.4.9 source to `main`.
2. Require the matching GitHub CodeQL run to pass.
3. Build and publish the v1.4.9 GitHub release and updater assets.
4. Verify the live manifest reports v1.4.9.
5. Create `Flare Fireplace Quotes v1.4.9 FULL BACKUP.zip` on the Desktop.
6. Silently update the app installed on the current computer and relaunch it.

## Maintained PowerShell workflows

```text
Build_And_Run_Safe.ps1
Run-Final-Release-Gate.ps1
Build_Release_Installer.ps1
Build_Publish_Professional_Release.ps1
```

`PUBLISH_v1.4.9.ps1` is a release-package launcher and is intentionally excluded from the committed repository and final production backup.

## Runtime data

Runtime data is stored under:

```text
%LOCALAPPDATA%\Flare Fireplace Quotes
```

Encrypted quote history:

```text
%LOCALAPPDATA%\Flare Fireplace Quotes\recent_quotes.json.dpapi
```

Gmail credentials:

```text
%LOCALAPPDATA%\Flare Fireplace Quotes\Credentials\gmail_credentials.json
```

Credentials, OAuth tokens, personal settings, logs, generated PDFs, build output, repository history, and temporary files are excluded from this source package.
