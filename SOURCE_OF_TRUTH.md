# Flare Fireplace Quotes Source of Truth

Version: **1.70.6** (2026 pricing foundation audit)

Repository: `kbAppDev/flare-fireplace-quotes-updates`

Updates are delivered through the app's startup update prompt. Release completion requires publishing the signed feed and verifying update detection from the previous installed version. Standalone installer downloads are not the normal user handoff.

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

v1.70.6 refreshes all four price tables from the supplied October 20, 2026 foundation while preserving the four media calculation sheets. It retains Kyle's confirmed $335 DuraFlex and $888 LC60H prices, and the distinct EH side-screen SKU where the supplied file contains conflicting data. The full comparison, source hash, and scope are recorded in docs/PRICING_AUDIT_1.70.6.md.

v1.70.3 is based on the published v1.70.2 source and corrects only the outdoor quote PDF disclaimer. Outdoor and outdoor see-through pages exclude installation costs without mentioning venting; indoor and indoor-outdoor pages retain their existing wording. Mixed quotes choose the disclaimer independently for each fireplace page. Email copy is unchanged.

v1.70.2 extends the published v1.70.1 release with complete TRA-BON-42/46 identity, exact base pricing, shared Traditional features, 45-inch premium media quantities, and Bonfire-specific product sheets. Its base and media prices were checked against the supplied August 2026 indoor MSRP PDF. It retains the approved Phone Link templates, encrypted unfinished-quote autosave, removal of live totals, smaller card images, photo removal, fireplace duplication, editable/deletable URLs with duplicate-label checks, rounded Windows frames, contextual help, and subtle Acrylic backgrounds. The requested indoor email opening and consultation link are included. Deleted links remain absent from drafts, empty cards support manual re-addition, and review changes survive navigation. Phone Link is launched through Microsoft's documented URI; final paste, review, and Send remain in Phone Link. No paid publisher signing has been added. The existing 1.6.9 safeguards cover pricing correctness, workbook/PDF processing, privacy,
Gmail recovery, release validation, and automatic update authenticity. It also adds a built-in system check,
keyboard fireplace reordering, unique quote numbers, and accessibility labels without changing quote data.

Publication remains fail-closed until the exact tagged commit passes direct source-format verification, rendered
Windows snapshots, warnings-as-errors builds, automated tests, dependency-vulnerability audit,
installer-integrity checks, and release-asset verification. CodeQL continues independently on source changes so
the publishing job does not need a security-events write token.
