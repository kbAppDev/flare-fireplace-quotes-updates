# Flare Fireplace Quotes Source of Truth

Publish-ready candidate: **1.6.7**

Repository: `kbAppDev/flare-fireplace-quotes-updates`

Pinned update manifest:

```text
https://github.com/kbAppDev/flare-fireplace-quotes-updates/releases/latest/download/flare-quotes-v1-latest.json
```

Current user-facing release deliverables:

```text
Flare.Fireplace.Quotes.exe
Flare.Fireplace.Quotes-portable.zip
flare-quotes-v1-latest.json
```

The pinned `flare-quotes-v1-latest.json` file is updater metadata. Regenerate it from the final installer's exact version, release URL, byte size, and SHA-256 before publication.

v1.6.7 preserves the approved interface and workflow while carrying each fireplace's optional location through the builder, quote PDF, URL verification, and Gmail resource links.

Publication remains fail-closed until the exact tagged commit passes direct source-format verification, rendered Windows snapshots, warnings-as-errors builds, automated tests, dependency-vulnerability audit, CodeQL, installer-integrity checks, and release-asset verification.
