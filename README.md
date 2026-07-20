# Flare Fireplace Quotes v1.4.10 Vent-Free Quote Hotfix

Publish-and-install-ready Windows WPF source prepared from the tested v1.4.10 release candidate.

## v1.4.10 fixes

- Preserves Outdoor Vent Free identity for regular-height `VFST`/`VST` models during resource resolution and URL verification.
- Uses the Outdoor Vent Free See Through style card for `VFST70` instead of the indoor See Through card.
- Resolves `VFST70` resources from the Ventless See Through resource family and prevents fallback to indoor/outdoor `ST-OD` links.
- Resolves regular-height Reflective Black Sides pricing through the actual `VF-RBS-ST` / `VFRBSST` price-book key.
- Restores the expected $208 MSRP for Reflective Black Sides on the tested `VFST70` quote.
- Extends regular-height alias handling so the same omitted-`R` suffix mismatch cannot affect equivalent front-facing or see-through pricing/resource lookups.
- Adds focused regression coverage for both `VST70` and `VFST70` resource and pricing behavior.

The v1.4.9 recipient-header fix remains in place, including copied-email normalization, stale-preview recipient refresh, final MIME validation, and regression coverage.

The deployment protections from v1.4.8 also remain in place: DPAPI-encrypted quote history, temporary-PDF cleanup, pinned GitHub updater paths, installer size verification, and SHA-256 verification.

Windows Authenticode publisher signing remains intentionally optional for this internal deployment lane.

## Functional validation completed

The reported `VFST70`, 16-inch quote was recreated successfully and confirmed to show:

- the Outdoor Vent Free See Through/VST style identity
- Ventless See Through resources instead of indoor/outdoor `ST-OD` resources
- Reflective Black Sides with the expected $208 MSRP

## Publish and install

Run `PUBLISH_v1.4.10.ps1` from this package. It will:

1. Push the exact clean v1.4.10 source to `main`.
2. Require the matching GitHub CodeQL run to pass.
3. Build and publish the v1.4.10 GitHub release and updater assets.
4. Verify the live manifest reports v1.4.10.
5. Create `Flare Fireplace Quotes v1.4.10 FULL BACKUP.zip` on the Desktop.
6. Silently update the app installed on the current computer and relaunch it.

## Maintained PowerShell workflows

```text
Build_And_Run_Safe.ps1
Run-Final-Release-Gate.ps1
Build_Release_Installer.ps1
Build_Publish_Professional_Release.ps1
```

`PUBLISH_v1.4.10.ps1` is a release-package launcher and is intentionally excluded from the committed repository and final production backup.

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
