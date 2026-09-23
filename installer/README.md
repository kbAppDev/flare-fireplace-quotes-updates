# Installer output

This directory is intentionally empty in the source candidate.

Publish the self-contained Windows application, then compile the checked-in Inno Setup definition directly:

```powershell
$releaseVersion = "1.6.9"
$publishDir = Join-Path (Get-Location) "FlareQuotes.App\bin\Release\net10.0-windows\win-x64\publish"
$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"

dotnet restore .\FlareQuotes.App\FlareQuotes.App.csproj -r win-x64 --locked-mode -p:PublishReadyToRun=true
dotnet publish .\FlareQuotes.App\FlareQuotes.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:SelfContained=true -p:PublishSingleFile=false -p:PublishReadyToRun=true
& $iscc "/DMyAppVersion=$releaseVersion" "/DSourceDir=$publishDir" "/FFlare.Fireplace.Quotes" .\FlareQuotes.App\Installer\FlareFireplacesQuotesInstaller.iss
Compress-Archive -Path "$publishDir\*" -DestinationPath .\installer\Flare.Fireplace.Quotes-portable.zip -CompressionLevel Optimal -Force
```

The automated release is authoritative. It additionally signs the update manifest, produces an SPDX SBOM,
creates a draft GitHub release, re-downloads and verifies every asset, and then publishes. Generated release output
is excluded from source control.
