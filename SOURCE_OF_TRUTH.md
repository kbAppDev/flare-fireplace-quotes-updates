# Flare Fireplace Quotes Source of Truth

Publish-ready version: **1.4.10**

Published production baseline before this release:

```text
v1.4.9
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

v1.4.10 correction scope:

- retain Outdoor Vent Free identity for regular-height VFST/VST resource resolution
- use Ventless See Through resources and the VST style card for VFST70
- prevent ST-OD fallback URLs for the tested vent-free see-through quote
- resolve regular-height Reflective Black Sides pricing from VF-RBS-ST / VFRBSST
- preserve the v1.4.9 Gmail recipient-header fix and all prior deployment hardening

Functional validation completed against a new VFST70 16-inch quote:

- correct Outdoor Vent Free See Through card
- correct Ventless/ST resource family
- no Data/OD or ST-OD fallback links
- Reflective Black Sides MSRP displayed as $208

Publication remains fail-closed until the exact v1.4.10 commit passes GitHub CodeQL and the live manifest verifies at version 1.4.10.
