param(
    [Parameter(Mandatory=$true)]
    [string]$InstallerPath,

    [Parameter(Mandatory=$true)]
    [string]$Version,

    [string]$Repo = "kbAppDev/flare-fireplace-quotes-updates",

    [string]$AssetName = "Flare.Fireplace.Quotes.exe",

    [string]$Notes = "Flare Fireplace Quotes update."
)

$ErrorActionPreference = "Stop"

if (!(Test-Path $InstallerPath)) {
    throw "Installer not found: $InstallerPath"
}

$OutputDir = Split-Path $InstallerPath -Parent
$ManifestPath = Join-Path $OutputDir "flare-quotes-v1-latest.json"
$Hash = (Get-FileHash -Path $InstallerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$Url = "https://github.com/$Repo/releases/latest/download/$AssetName"

$Manifest = [ordered]@{
    version = $Version
    installer = $Url
    url = $Url
    sha256 = $Hash
    notes = $Notes
}

$Json = $Manifest | ConvertTo-Json -Depth 10
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($ManifestPath, $Json, $Utf8NoBom)

Write-Host "Created:" -ForegroundColor Green
Write-Host $ManifestPath
Write-Host "URL: $Url" -ForegroundColor Cyan
Write-Host "SHA-256: $Hash" -ForegroundColor Cyan
