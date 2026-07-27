# Flare Quotes v1.6.3 — Audited UI Refresh

This candidate applies the three-pane visual redesign while keeping the official v1.5.1 pricing, PDF, Gmail, updater, security, parsing, feature/media, workbook, and release-pipeline files byte-equivalent after line-ending normalization.

## Production UI changes

- Workflow stepper moved into the title bar.
- Customer request, current-fireplace builder, and quote fireplace summary are visible together.
- Fireplace cards can be edited and safely replaced in place. Canceling an edit retains the original.
- Guarded burn-away animation for mouse removal. Keyboard removal remains immediate and accessible.
- Readiness, count, and estimate indicators invalidate when quote inputs change.
- Settings includes a manual update availability check. Installation remains in the verified startup updater flow.

## Required release validation

The source is not publishable merely because the XAML is well formed. The Windows release candidate must pass:

1. `Test-UiContract.ps1`
2. restore and NuGet vulnerability audit
3. warnings-as-errors builds
4. automated tests
5. `Test-UiSnapshots.ps1` at normal and minimum dimensions
6. CodeQL
7. local installer installation and hands-on smoke test
8. tagged release workflow and live manifest/hash verification
