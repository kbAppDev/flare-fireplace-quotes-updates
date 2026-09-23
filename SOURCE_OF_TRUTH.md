# Flare Fireplace Quotes Source of Truth

Publish-ready candidate: **1.6.9**

Repository: `kbAppDev/flare-fireplace-quotes-updates`

Pinned update manifest:

```text
https://github.com/kbAppDev/flare-fireplace-quotes-updates/releases/latest/download/flare-quotes-v2-latest.json
```

Current user-facing release deliverables:

```text
Flare.Fireplace.Quotes.exe
Flare.Fireplace.Quotes-portable.zip
flare-quotes-v2-latest.json
flare-quotes-v1-latest.json (legacy updater compatibility only)
Flare.Fireplace.Quotes.spdx.json
THIRD-PARTY-NOTICES.md
```

The pinned `flare-quotes-v2-latest.json` file is mandatory-signed updater metadata. The automated workflow creates it
from the final installer's exact version, release URL, byte size, and SHA-256, verifies the signature and assets in
a draft release, and only then publishes. The v1 file remains solely to bootstrap existing v1.6.8 installations.

v1.6.9 preserves the approved workflow while hardening pricing correctness, workbook/PDF processing, privacy,
Gmail recovery, release validation, and automatic update authenticity. It also adds a built-in system check,
keyboard fireplace reordering, unique quote numbers, and accessibility labels without changing quote data.

Publication remains fail-closed until the exact tagged commit passes direct source-format verification, rendered
Windows snapshots, warnings-as-errors builds, automated tests, dependency-vulnerability audit,
installer-integrity checks, and release-asset verification. CodeQL continues independently on source changes so
the publishing job does not need a security-events write token.
