# Flare Fireplace Quotes v1.6.9

Windows WPF application for turning fireplace quote requests into priced PDFs, reviewed specification links, and Gmail drafts.

## Release highlights

v1.6.9 is a focused correctness, resilience, and update-security release. Pricing now fails closed when a selected item cannot be matched, workbooks and generated PDFs are processed with strict resource and content limits, sensitive data handling is tighter, and releases use a mandatory signed update manifest.

The updater is pinned to the Flare-managed GitHub release lane. Every update manifest must carry a valid RS256 signature from the public key embedded in the application. Every installer download must also match the release version, exact asset path, declared byte size, and SHA-256 hash before launch.

## Build and validate

Requirements: Windows, .NET 10 SDK, and Inno Setup 6 for installer builds.

```powershell
dotnet restore .\FlareQuotes.sln
dotnet format .\FlareQuotes.sln --verify-no-changes --no-restore --verbosity minimal
dotnet build .\FlareQuotes.App\FlareQuotes.App.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build .\FlareQuotes.Tests\FlareQuotes.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test .\FlareQuotes.Tests\FlareQuotes.Tests.csproj -c Release
.\scripts\Test-Coverage.ps1
dotnet list .\FlareQuotes.App\FlareQuotes.App.csproj package --vulnerable --include-transitive --format json --no-restore
dotnet list .\FlareQuotes.Tests\FlareQuotes.Tests.csproj package --vulnerable --include-transitive --format json --no-restore
```

The Gmail end-to-end test is reported as skipped because it creates and deletes real drafts. Run it only during a
credentialed manual verification. The coverage script runs the complete offline suite under Coverlet and enforces the
checked-in 45% aggregate line-coverage floor across production assemblies.

## Create the release deliverables

Run these commands from the repository root after the validation commands pass:

```powershell
$releaseVersion = "1.6.9"
$publishDir = Join-Path (Get-Location) "FlareQuotes.App\bin\Release\net10.0-windows\win-x64\publish"
$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"

dotnet restore .\FlareQuotes.App\FlareQuotes.App.csproj -r win-x64 --locked-mode -p:PublishReadyToRun=true
dotnet publish .\FlareQuotes.App\FlareQuotes.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:SelfContained=true -p:PublishSingleFile=false -p:PublishReadyToRun=true
& $iscc "/DMyAppVersion=$releaseVersion" "/DSourceDir=$publishDir" "/FFlare.Fireplace.Quotes" .\FlareQuotes.App\Installer\FlareFireplacesQuotesInstaller.iss
Compress-Archive -Path "$publishDir\*" -DestinationPath .\installer\Flare.Fireplace.Quotes-portable.zip -CompressionLevel Optimal -Force
```

If Inno Setup is installed elsewhere, replace `$iscc` with the full path to `ISCC.exe`. The Inno command writes `installer\Flare.Fireplace.Quotes.exe`.

## Publishing

Publish only from a clean, passing commit tagged `v1.6.9`, matching `Directory.Build.props`. The tag-driven GitHub
workflow performs formatting, tests, rendered Windows UI checks, packaging, manifest signing, and installer hash
verification before publishing. CodeQL continues independently on source changes. The updater metadata points to
that exact versioned installer and records its exact size and SHA-256.

Current user-facing release deliverables:

- `Flare.Fireplace.Quotes.exe`
- `Flare.Fireplace.Quotes-portable.zip`
- `flare-quotes-v2-latest.json` (signed update feed)
- `flare-quotes-v1-latest.json` (legacy v1.6.8 compatibility feed)
- `Flare.Fireplace.Quotes.spdx.json`
- `THIRD-PARTY-NOTICES.md`

The update manifests are metadata, not application packages. The tag-driven workflow generates them from the
verified installer, signs the v2 feed, and validates all draft assets before publication.

## Runtime data

User data is kept outside the installation under `%LOCALAPPDATA%\Flare Fireplace Quotes`. Quote history and Gmail OAuth tokens use Windows DPAPI with CurrentUser scope. Credentials, tokens, user settings, logs, generated PDFs, and build output must never be committed or placed in source-only archives.
