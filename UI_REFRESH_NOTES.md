# Flare Quotes v1.6.8 — Audited UI Refresh

This candidate retains the audited three-pane design and adds a compact quantity selector for repeated, identical fireplace configurations.

## Production UI changes

- Workflow stepper moved into the title bar.
- Customer request, current-fireplace builder, and quote fireplace summary are visible together.
- Fireplace cards can be edited and safely replaced in place. Canceling an edit retains the original.
- Each saved fireplace card shows its configuration quantity, and the quote count reflects the total number of physical fireplaces.
- Guarded burn-away animation for mouse removal. Keyboard removal remains immediate and accessible.
- Readiness, count, and estimate indicators invalidate when quote inputs change.
- Settings includes a manual update availability check. Installation remains in the verified startup updater flow.

## Required release validation

The source is not publishable merely because the XAML is well formed. The Windows release candidate must pass:

1. `dotnet restore .\FlareQuotes.sln`
2. `dotnet format .\FlareQuotes.sln --verify-no-changes --no-restore --verbosity minimal`
3. direct and transitive NuGet vulnerability audits for the app and test projects
4. warnings-as-errors Release builds for the app and tests
5. automated tests excluding the opt-in live Gmail integration suite
6. a direct snapshot-mode run at normal and minimum dimensions:

   ```powershell
   $env:FLARE_UI_SNAPSHOT_MODE = "1"
   $env:FLARE_UI_SNAPSHOT_DIR = Join-Path (Get-Location) "artifacts\ui-snapshots"
   dotnet run --project .\FlareQuotes.App\FlareQuotes.App.csproj -c Release --no-restore -p:DefineConstants=FLARE_UI_SNAPSHOTS -p:TreatWarningsAsErrors=true
   Remove-Item Env:FLARE_UI_SNAPSHOT_MODE, Env:FLARE_UI_SNAPSHOT_DIR -ErrorAction SilentlyContinue
   ```

7. CodeQL
8. direct Inno Setup compilation, local installer installation, and hands-on smoke testing
9. installer size/SHA-256 verification and live updater-metadata verification
