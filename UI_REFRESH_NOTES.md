# Flare Quotes v1.6.9 — Hardened UI Release

This release retains the audited three-pane design, integrates configuration quantities, and hardens the update and
release gates around the production interface.

## Production UI changes

- Workflow stepper moved into the title bar.
- Customer request, current-fireplace builder, and quote fireplace summary are visible together.
- Fireplace cards can be edited and safely replaced in place. Canceling an edit retains the original.
- Each saved fireplace card shows its configuration quantity, and the quote count reflects the total number of physical fireplaces.
- Guarded burn-away animation for mouse removal. Keyboard removal remains immediate and accessible.
- Readiness, count, and estimate indicators invalidate when quote inputs change.
- Settings includes a manual update check that reports failures and opens the same signed update prompt used at startup.
- Startup and manual checks accept only the pinned Flare release channel, a valid signed manifest, and an installer
  whose version, URL, size, and SHA-256 match that manifest.

## Required release validation

The source is not publishable merely because the XAML is well formed. The Windows release candidate must pass:

1. `dotnet restore .\FlareQuotes.sln --locked-mode`
2. `dotnet format .\FlareQuotes.sln --verify-no-changes --no-restore --verbosity minimal`
3. direct and transitive NuGet vulnerability audits for the app and test projects
4. warnings-as-errors Release builds for the app and tests
5. automated tests excluding the opt-in live Gmail integration suite
6. the bounded snapshot gate at normal and minimum dimensions:

   ```powershell
   .\scripts\Invoke-UiSnapshotGate.ps1 -OutputPath artifacts/ui-snapshots -TimeoutSeconds 120
   ```

   The gate terminates a hung capture, validates every expected image and layout-metrics file, and proves the
   representative quantity-three and Passive Heat Flex states were rendered.

7. the checked-in 45% production line-coverage gate
8. CodeQL
9. direct Inno Setup compilation, local installer installation, and hands-on smoke testing
10. signed manifest, installer size/SHA-256, and live updater-metadata verification
