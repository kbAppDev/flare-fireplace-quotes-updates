# Flare Fireplace Quotes Source of Truth

Publish-ready candidate: **1.6.0**

Repository: `kbAppDev/flare-fireplace-quotes-updates`

Pinned update manifest:

```text
https://github.com/kbAppDev/flare-fireplace-quotes-updates/releases/latest/download/flare-quotes-v1-latest.json
```

Required release assets:

```text
Flare.Fireplace.Quotes.exe
flare-quotes-v1-latest.json
Flare.Fireplace.Quotes-portable.zip
```

v1.6.0 refreshes the WPF presentation and adds safe fireplace-card editing while preserving the audited production business, data, Gmail, PDF, updater, and security layers from v1.5.1. Its scope is defined in `RELEASE_NOTES.md`.

Publication remains fail-closed until the exact tagged commit passes UI-contract validation, rendered Windows snapshots, warnings-as-errors builds, automated tests, dependency-vulnerability audit, CodeQL, installer-integrity checks, and live-manifest verification.
