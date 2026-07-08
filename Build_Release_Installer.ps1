param(
    [Parameter(Mandatory = $false)]
    [string]$Version = "",

    [Parameter(Mandatory = $false)]
    [string]$Repo = "kbAppDev/flare-fireplace-quotes-updates",

    [Parameter(Mandatory = $false)]
    [string]$Notes = ""
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root
[System.IO.Directory]::SetCurrentDirectory($Root)

$ManifestAssetName = "flare-quotes-v1-latest.json"
$InstallerAssetName = "Flare.Fireplace.Quotes.exe"
$LocalInstallerName = "Flare Fireplace Quotes.exe"

$InstallerUrl = "https://github.com/$Repo/releases/latest/download/$InstallerAssetName"

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content ".\Directory.Build.props" -Raw
    $Version = ($props.Project.PropertyGroup | Select-Object -First 1).Version
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Could not determine Version. Pass -Version or set Directory.Build.props."
}

$versionParts = $Version.Split(".")
while ($versionParts.Count -lt 3) {
    $versionParts += "0"
}
$Version4 = "$($versionParts[0]).$($versionParts[1]).$($versionParts[2]).0"

if ([string]::IsNullOrWhiteSpace($Notes)) {
    $Notes = "Flare Fireplace Quotes v$Version update."
}

Write-Host ""
Write-Host "============================================================"
Write-Host "BUILD RELEASE v$Version"
Write-Host "============================================================"

Write-Host ""
Write-Host "Stopping running app instances..." -ForegroundColor Cyan
Get-Process | Where-Object {
    $_.ProcessName -like "*Flare*" -or
    $_.MainWindowTitle -like "*Flare Fireplace Quotes*"
} | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Finding Inno Setup compiler..." -ForegroundColor Cyan

$IsccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)

$Iscc = $null

foreach ($candidate in $IsccCandidates) {
    if ($candidate -and (Test-Path $candidate)) {
        $Iscc = $candidate
        break
    }
}

if (-not $Iscc) {
    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source -and (Test-Path $cmd.Source)) {
        $Iscc = $cmd.Source
    }
}

if (-not $Iscc) {
    throw "ISCC.exe was not found. Install Inno Setup 6, then rerun this script."
}

Write-Host "Using ISCC:" -ForegroundColor Green
Write-Host $Iscc

$AppProject = Join-Path $Root "FlareQuotes.App\FlareQuotes.App.csproj"
$IssFile = Join-Path $Root "FlareQuotes.App\Installer\FlareFireplacesQuotesInstaller.iss"
$PublishDir = Join-Path $Root "FlareQuotes.App\bin\Release\net10.0-windows\win-x64\publish"
$InstallerDir = Join-Path $Root "installer"
$LocalInstallerPath = Join-Path $InstallerDir $LocalInstallerName
$UploadInstallerPath = Join-Path $InstallerDir $InstallerAssetName
$ManifestPath = Join-Path $InstallerDir $ManifestAssetName

if (!(Test-Path $AppProject)) {
    throw "App project not found: $AppProject"
}

if (!(Test-Path $IssFile)) {
    throw "Inno Setup script not found: $IssFile"
}

Write-Host ""
Write-Host "Cleaning previous publish/installer output..." -ForegroundColor Cyan

Remove-Item ".\FlareQuotes.App\bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item ".\FlareQuotes.App\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item ".\FlareQuotes.Core\bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item ".\FlareQuotes.Core\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item ".\FlareQuotes.Infrastructure\bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item ".\FlareQuotes.Infrastructure\obj" -Recurse -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Path $InstallerDir -Force | Out-Null
Remove-Item $LocalInstallerPath -Force -ErrorAction SilentlyContinue
Remove-Item $UploadInstallerPath -Force -ErrorAction SilentlyContinue
Remove-Item $ManifestPath -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Restoring packages..." -ForegroundColor Cyan
dotnet restore $AppProject

if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed."
}

Write-Host ""
Write-Host "Publishing self-contained Windows app..." -ForegroundColor Cyan
dotnet publish $AppProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=true `
    -p:Version=$Version `
    -p:AssemblyVersion=$Version4 `
    -p:FileVersion=$Version4 `
    -p:InformationalVersion=$Version `
    -p:ProductVersion=$Version

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$PublishExe = Join-Path $PublishDir "Flare Fireplace Quotes.exe"

if (!(Test-Path $PublishExe)) {
    throw "Published EXE not found: $PublishExe"
}

$PublishInfo = (Get-Item $PublishExe).VersionInfo

Write-Host ""
Write-Host "Published app version check:" -ForegroundColor Cyan
Write-Host "ProductVersion: $($PublishInfo.ProductVersion)"
Write-Host "FileVersion:    $($PublishInfo.FileVersion)"

if (($PublishInfo.ProductVersion -notlike "$Version*") -and ($PublishInfo.FileVersion -notlike "$Version*")) {
    throw "Published app EXE does not report $Version. Refusing to build/publish a recycled installer."
}

Write-Host ""
Write-Host "Building installer..." -ForegroundColor Cyan

& $Iscc `
    "/DMyAppVersion=1.4.5
    "/DSourceDir=$PublishDir" `
    $IssFile

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup installer build failed."
}

if (!(Test-Path $LocalInstallerPath)) {
    throw "Installer was not found: $LocalInstallerPath"
}

Copy-Item $LocalInstallerPath $UploadInstallerPath -Force

Write-Host ""
Write-Host "Creating GitHub update manifest..." -ForegroundColor Cyan

$Sha256 = (Get-FileHash -Path $UploadInstallerPath -Algorithm SHA256).Hash.ToLowerInvariant()

$Manifest = [ordered]@{
    version = $Version
    installer = $InstallerUrl
    url = $InstallerUrl
    sha256 = $Sha256
    notes = $Notes
}

$Json = $Manifest | ConvertTo-Json -Depth 10
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($ManifestPath, $Json, $Utf8NoBom)

Write-Host ""
Write-Host "Release build complete." -ForegroundColor Green
Write-Host ""
Write-Host "GitHub release assets ready:" -ForegroundColor Yellow
Write-Host $UploadInstallerPath
Write-Host $ManifestPath
Write-Host ""
Write-Host "SHA-256:" -ForegroundColor Cyan
Write-Host $Sha256

# FLARE_CLEAN_DUPLICATE_INSTALLER_OUTPUT
# Keep only the GitHub updater asset name in /installer after Inno builds.
$InstallerDirForCleanup = Join-Path $PSScriptRoot "installer"
$GitHubInstallerAsset = Join-Path $InstallerDirForCleanup "Flare.Fireplace.Quotes.exe"
$ExtraSpaceNamedInstaller = Join-Path $InstallerDirForCleanup "Flare Fireplace Quotes.exe"

if ((Test-Path $GitHubInstallerAsset) -and (Test-Path $ExtraSpaceNamedInstaller)) {
    $githubHash = (Get-FileHash $GitHubInstallerAsset -Algorithm SHA256).Hash
    $extraHash = (Get-FileHash $ExtraSpaceNamedInstaller -Algorithm SHA256).Hash

    if ($githubHash -eq $extraHash) {
        Remove-Item $ExtraSpaceNamedInstaller -Force
        Write-Host "Removed duplicate installer output: $ExtraSpaceNamedInstaller"
    }
    else {
        Write-Warning "Duplicate installer names were found, but hashes differ. Keeping both for inspection."
    }
}
elseif ((-not (Test-Path $GitHubInstallerAsset)) -and (Test-Path $ExtraSpaceNamedInstaller)) {
    Copy-Item $ExtraSpaceNamedInstaller $GitHubInstallerAsset -Force
    Write-Host "Copied installer to GitHub updater asset name: $GitHubInstallerAsset"
}







