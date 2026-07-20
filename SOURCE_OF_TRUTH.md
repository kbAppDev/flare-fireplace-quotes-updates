# Flare Fireplace Quotes Source of Truth

Publish-ready version: **1.5.0**

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

v1.5.0 modernizes the WPF presentation layer while preserving the audited v1.4.12 business behavior and security controls. Its scope is defined in `RELEASE_NOTES.md` and enforced by automated build, test, dependency-vulnerability, CodeQL, installer-integrity, and live-manifest checks.

Publication remains fail-closed until the exact tagged commit passes the GitHub release workflow and the live manifest reports version 1.5.0 with the matching installer size and SHA-256 hash.
