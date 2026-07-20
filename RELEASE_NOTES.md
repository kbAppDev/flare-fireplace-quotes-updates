Flare Fireplace Quotes v1.4.12 is a release-verification patch for the audited v1.4.11 security and reliability release.

- Carries forward every v1.4.11 credential, updater, workbook, settings, logging, UI, test, and release-pipeline hardening change.
- Corrects the cache-busted public-manifest verification request by constructing it with `System.UriBuilder` instead of ambiguous PowerShell string interpolation.
- Refreshes the pinned CodeQL action to the current immutable Node.js 24-compatible v4 commit.
- Leaves quote generation, Gmail drafting, pricing data, resource workbooks, and end-user workflows unchanged from v1.4.11.
