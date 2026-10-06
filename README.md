# Flare Fireplace Quotes v1.70.1

Windows WPF application for turning fireplace quote requests into priced PDFs, reviewed specification links, and Gmail drafts.

## Release highlights

v1.70.1 expands the requested 1.70 update with an integrated text-message template manager, encrypted unfinished-quote autosave, fireplace duplication, individual photo removal, and editable specification URLs with duplicate-name validation. Live totals and their background pricing work have been removed. Smaller product-image assets, contextual help bubbles, native rounded Windows frames, and restrained Acrylic backgrounds streamline the interface. Indoor email drafts use the requested opening and exact HubSpot project consultation link. This update starts from the verified GitHub v1.6.9 tag and retains its pricing, privacy, and update safeguards.

The Text message button uses the current quote's customer details. Immediately after creating a Gmail draft it uses that completed quote for follow-up. Templates support `{FirstName}`, `{Project}`, `{Model}`, `{Consultation}`, and `{SalesName}`. Preview edits affect only the current message; New, Edit, Delete, and Undo manage reusable templates. Copy the message and number, open Phone Link, and paste, review, and send there. Phone Link does not provide a documented recipient-and-body prefill API, so this workflow uses Microsoft's documented launch URI and requires the final Send action in Phone Link.

Unfinished quotes restore into Review after restart. Customer fields, saved fireplaces, unfinished fireplace edits, photo references, and URL-review changes are recovered; PDFs and pricing are regenerated from current data. Clear and successful Gmail draft creation discard the unfinished autosave. Photo removal detaches a file from the quote without deleting the original. Specification URL changes affect the email link list; regenerate the PDF to refresh its separately generated contents.

Windows 11 supplies rounded native window corners and a subtle Acrylic backdrop. Cards and fields remain opaque for legibility; older Windows versions and high-contrast mode use an opaque fallback. Product card PNGs retain their transparent backgrounds and now total approximately 3.4 MB instead of 27.5 MB.

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
$releaseVersion = "1.70.1"
$publishDir = Join-Path (Get-Location) "FlareQuotes.App\bin\Release\net10.0-windows\win-x64\publish"
$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"

dotnet restore .\FlareQuotes.App\FlareQuotes.App.csproj -r win-x64 --locked-mode -p:PublishReadyToRun=true
dotnet publish .\FlareQuotes.App\FlareQuotes.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:SelfContained=true -p:PublishSingleFile=false -p:PublishReadyToRun=true
& $iscc "/DMyAppVersion=$releaseVersion" "/DSourceDir=$publishDir" "/FFlare.Fireplace.Quotes" .\FlareQuotes.App\Installer\FlareFireplacesQuotesInstaller.iss
Compress-Archive -Path "$publishDir\*" -DestinationPath .\installer\Flare.Fireplace.Quotes-portable.zip -CompressionLevel Optimal -Force
```

If Inno Setup is installed elsewhere, replace `$iscc` with the full path to `ISCC.exe`. The Inno command writes `installer\Flare.Fireplace.Quotes.exe`.

## Publishing

Publish only from a clean, passing commit tagged `v1.70.1`, matching `Directory.Build.props`. The tag-driven GitHub
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

User data is kept outside the installation under `%LOCALAPPDATA%\Flare Fireplace Quotes`. Quote history, unfinished quote autosaves, message templates, and Gmail OAuth tokens use Windows DPAPI with CurrentUser scope. Personalized text-message previews are temporary and are not saved to the template library. Credentials, tokens, user settings, logs, generated PDFs, and build output must never be committed or placed in source-only archives.
