# Flare Fireplace Quotes v1.6.7

Windows WPF application for turning fireplace quote requests into priced PDFs, verified specification links, and Gmail drafts.

## Release highlights

v1.6.7 adds clear per-fireplace location names throughout multi-fireplace quotes while retaining the v1.6.6 pricing, media, and estimated-total fixes.

The updater is pinned to the Flare-managed GitHub release lane. Every installer download must match the release version, exact asset path, declared byte size, and SHA-256 hash before launch. Optional RS256 manifest signatures fail closed whenever a signature is present but invalid.

## Build and validate

Requirements: Windows, .NET 10 SDK, and Inno Setup 6 for installer builds.

```powershell
dotnet restore .\FlareQuotes.sln
dotnet format .\FlareQuotes.sln --verify-no-changes --no-restore --verbosity minimal
dotnet build .\FlareQuotes.App\FlareQuotes.App.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build .\FlareQuotes.Tests\FlareQuotes.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test .\FlareQuotes.Tests\FlareQuotes.Tests.csproj -c Release --filter "FullyQualifiedName!~GmailEveryModelIntegrationTests"
dotnet list .\FlareQuotes.App\FlareQuotes.App.csproj package --vulnerable --include-transitive --format json --no-restore
dotnet list .\FlareQuotes.Tests\FlareQuotes.Tests.csproj package --vulnerable --include-transitive --format json --no-restore
```

## Create the release deliverables

Run these commands from the repository root after the validation commands pass:

```powershell
$releaseVersion = "1.6.7"
$publishDir = Join-Path (Get-Location) "FlareQuotes.App\bin\Release\net10.0-windows\win-x64\publish"
$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"

dotnet restore .\FlareQuotes.App\FlareQuotes.App.csproj -r win-x64
dotnet publish .\FlareQuotes.App\FlareQuotes.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:SelfContained=true -p:PublishSingleFile=false -p:PublishReadyToRun=true
& $iscc "/DMyAppVersion=$releaseVersion" "/DSourceDir=$publishDir" "/FFlare.Fireplace.Quotes" .\FlareQuotes.App\Installer\FlareFireplacesQuotesInstaller.iss
Compress-Archive -Path "$publishDir\*" -DestinationPath .\installer\Flare.Fireplace.Quotes-portable.zip -CompressionLevel Optimal -Force
```

If Inno Setup is installed elsewhere, replace `$iscc` with the full path to `ISCC.exe`. The Inno command writes `installer\Flare.Fireplace.Quotes.exe`.

## Publishing

Publish only from a clean, passing commit tagged `v1.6.7`, matching `Directory.Build.props`. The tag-driven GitHub workflow performs formatting, tests, rendered Windows UI checks, CodeQL, packaging, and installer hash verification before publishing. The updater metadata points to that exact versioned installer and records its exact size and SHA-256.

Current user-facing release deliverables:

- `Flare.Fireplace.Quotes.exe`
- `Flare.Fireplace.Quotes-portable.zip`
- `flare-quotes-v1-latest.json`

The updater's `flare-quotes-v1-latest.json` is release metadata, not an additional application package. Regenerate and verify it against the installer before publication.

## Runtime data

User data is kept outside the installation under `%LOCALAPPDATA%\Flare Fireplace Quotes`. Quote history and Gmail OAuth tokens use Windows DPAPI with CurrentUser scope. Credentials, tokens, user settings, logs, generated PDFs, and build output must never be committed or placed in source-only archives.
