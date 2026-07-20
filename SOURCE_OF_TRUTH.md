# Flare Fireplace Quotes Source of Truth

Publish-ready version: **1.4.9**

Published production baseline before this release:

```text
v1.4.8
commit e7da99dabc9d75db9083d6fb649b61060c3483bf
```

Repository:

```text
kbAppDev/flare-fireplace-quotes-updates
```

Update manifest lane:

```text
https://github.com/kbAppDev/flare-fireplace-quotes-updates/releases/latest/download/flare-quotes-v1-latest.json
```

Required release assets:

```text
Flare.Fireplace.Quotes.exe
flare-quotes-v1-latest.json
```

Completed local v1.4.9 validation:

- clean Release build with warnings treated as errors
- 20 unit, regression, email-normalization, MIME-recipient, DPAPI-history, and updater-policy tests
- dependency vulnerability and source-security scans
- live Gmail create-confirm-delete validation for all 302 canonical fireplace models
- manual reproduction using a newly auto-filled FF60H quote addressed to the reported external Gmail recipient

Publication remains fail-closed until the exact v1.4.9 commit passes GitHub CodeQL and the live manifest verifies at version 1.4.9.
