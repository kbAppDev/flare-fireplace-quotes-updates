# Flare Fireplace Quotes Source of Truth

Publish-ready version: **1.4.12**

Repository: `kbAppDev/flare-fireplace-quotes-updates`

Pinned update manifest:

```text
https://github.com/kbAppDev/flare-fireplace-quotes-updates/releases/latest/download/flare-quotes-v1-latest.json
```

Required release assets:

```text
Flare.Fireplace.Quotes.exe
flare-quotes-v1-latest.json
```

v1.4.12 carries forward the audited v1.4.11 security and reliability release and corrects its release pipeline's cache-busted live-manifest request. Its scope is defined in `RELEASE_NOTES.md` and enforced by automated build, test, dependency-vulnerability, CodeQL, installer-integrity, and live-manifest checks.

Publication remains fail-closed until the exact tagged commit passes the GitHub release workflow and the live manifest reports version 1.4.12 with the matching installer size and SHA-256 hash.
