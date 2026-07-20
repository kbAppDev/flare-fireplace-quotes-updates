# Flare Fireplace Quotes v1.5.0

Windows WPF application for turning fireplace quote requests into priced PDFs, verified specification links, and Gmail drafts.

## Release highlights

v1.5.0 adds a modern, streamlined Windows interface on top of the audited v1.4.12 application. The quote, pricing, PDF, URL-verification, Gmail, settings, history, and updater behavior remain unchanged. See `RELEASE_NOTES.md` for the complete summary.

The updater is pinned to the Flare-managed GitHub release lane. Every installer download must match the release version, exact asset path, declared byte size, and SHA-256 hash before launch. Optional RS256 manifest signatures fail closed whenever a signature is present but invalid.

## Build and test

Requirements: Windows, .NET 10 SDK, and Inno Setup 6 for installer builds.

```powershell
dotnet restore .\FlareQuotes.Tests\FlareQuotes.Tests.csproj
dotnet build .\FlareQuotes.App\FlareQuotes.App.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test .\FlareQuotes.Tests\FlareQuotes.Tests.csproj -c Release --filter "FullyQualifiedName!~GmailEveryModelIntegrationTests"
```

Maintained workflows:

- `Build_And_Run_Safe.ps1` — local clean build and launch.
- `Test-UiContract.ps1` — validates required workflow bindings, commands, named controls, and theme resources.
- `Run-Final-Release-Gate.ps1` — full pre-release validation.
- `Build_Release_Installer.ps1` — self-contained Windows installer and updater manifest.
- `Build_Publish_Professional_Release.ps1` — validated installer build and GitHub release publication.
- `.github/workflows/release.yml` — tag-driven Windows build, test, CodeQL, installer, manifest, and release pipeline.

## Publishing

Merge a clean, passing commit to `main`, then push a tag matching `Directory.Build.props`, such as `v1.5.0`. The release workflow refuses mismatched versions, vulnerable NuGet dependencies, compiler warnings, test failures, or CodeQL failures before publishing updater assets.

Required release assets:

- `Flare.Fireplace.Quotes.exe`
- `flare-quotes-v1-latest.json`
- `Flare.Fireplace.Quotes-portable.zip`

## Runtime data

User data is kept outside the installation under `%LOCALAPPDATA%\Flare Fireplace Quotes`. Quote history and Gmail OAuth tokens use Windows DPAPI with CurrentUser scope. Credentials, tokens, user settings, logs, generated PDFs, and build output must never be committed or placed in source-only archives.
