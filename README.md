# Flare Fireplace Quotes v1.4.7

Clean canonical source for the Windows WPF quote application.

## Included

- Application, core, infrastructure, and test projects
- Required price and resource workbooks
- Installer definition and supported build/release scripts
- Every-model Gmail integration test covering 302 fireplace models
- CodeQL workflow

## Runtime data

Runtime data is stored under:

```text
%LOCALAPPDATA%\Flare Fireplace Quotes
```

Gmail credentials are restricted to:

```text
%LOCALAPPDATA%\Flare Fireplace Quotes\Credentials\gmail_credentials.json
```

Credentials, tokens, settings, logs, generated PDFs, build output, and installer output are not included in source control.

## Attachment behavior

The application never searches the website or WordPress for fireplace images. Drafts contain the generated PDF and only local images explicitly selected by the user.

## Build and test

```powershell
.\Build_And_Run_Safe.ps1
.\Run-Final-Release-Gate.ps1
```

## Publish v1.4.7

Run `PUBLISH_v1.4.7.ps1` from this folder. It pushes the clean source to `kbAppDev/flare-fireplace-quotes-updates`, waits for CodeQL, builds the Windows installer, creates the GitHub release assets, verifies the live update manifest, and creates a full backup ZIP on the Desktop.
