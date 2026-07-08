# Flare Fireplace Quotes

C# / WPF desktop application for creating Flare Fireplace quote PDFs, verifying product/spec URLs, and creating Gmail drafts.

## Current app lane

- Framework: .NET 10 Windows / WPF
- App source folder: C:\Users\kyle\OneDrive\Desktop\Flare Quotes
- Installed app path: C:\Users\kyle\AppData\Local\Programs\Flare Fireplace Quotes\Flare Fireplace Quotes.exe
- Settings path: C:\Users\kyle\AppData\Local\Flare Fireplace Quotes\settings.json
- Update manifest: https://github.com/kbAppDev/flare-fireplace-quotes-updates/releases/latest/download/flare-quotes-v1-latest.json
- GitHub repo: kbAppDev/flare-fireplace-quotes-updates

## Maintained structure

`	ext
FlareQuotes.App
FlareQuotes.Core
FlareQuotes.Infrastructure
FlareQuotes.Tests
LocalData
docs
installer
`

## Required local data

`	ext
LocalData\pricing.xlsx
LocalData\resource_links.xlsx
LocalData\outdoor_spec_center_extracted_links.xlsx
`

gmail_credentials.json may be used locally for Gmail OAuth testing, but it must never be committed, uploaded, or shared.

## Build and run

`powershell
cd "C:\Users\kyle\OneDrive\Desktop\Flare Quotes"

Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
Unblock-File ".\Build_And_Run_Safe.ps1"
.\Build_And_Run_Safe.ps1
`

## Release assets

GitHub release assets must remain exactly:

`	ext
Flare.Fireplace.Quotes.exe
flare-quotes-v1-latest.json
`

Always publish a new higher version. Do not edit an old release to simulate an update.

## Security rules

Do not commit or share:

`	ext
gmail_credentials.json
token files
settings.json
generated PDFs
logs
bin/
obj/
WebView2 runtime cache
installer output artifacts
`

Gmail tokens are protected using Windows DPAPI in the local user profile.

## Notes

The app should keep heavy work lazy-loaded so the main window opens quickly. Build output and WebView2 cache are reproducible runtime artifacts and should stay out of source backups.
