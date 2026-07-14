# Flare Fireplace Quotes Source of Truth

Release version: **1.4.8**

Validated production baseline:

```text
v1.4.7
commit 673e7f419b337e29d3cf7bdb6a2fc3c8f1375761
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

v1.4.8 publication is permitted only after all of the following pass:

- clean Release build with warnings treated as errors
- unit, regression, DPAPI-history, and updater-policy tests
- dependency vulnerability and source security scans
- live Gmail create-confirm-delete validation for all 302 canonical fireplace models
- GitHub CodeQL analysis
- published manifest verification at version 1.4.8
