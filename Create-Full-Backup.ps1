param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Repo = "kbAppDev/flare-fireplace-quotes-updates"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$tag = "v$Version"
$assetName = "Flare.Fireplace.Quotes-v$Version-FULL-BACKUP.zip"
$outputPath = Join-Path $PSScriptRoot "installer\$assetName"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    "FlareFullBackup-" + [Guid]::NewGuid().ToString("N"))
$sourceZip = Join-Path $tempRoot "source.zip"
$bundleRoot = Join-Path $tempRoot "bundle"
$sourceRoot = Join-Path $bundleRoot "Source"
$releaseRoot = Join-Path $bundleRoot "Release"

function Write-Utf8NoBom([string]$Path, [string]$Text) {
    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

try {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI is required."
    }

    gh auth status | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI is not authenticated."
    }

    $commit = (git rev-parse "$tag^{commit}").Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') {
        throw "Could not resolve exact commit for $tag."
    }

    New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path $outputPath -Parent) -Force | Out-Null

    git archive --format=zip --output="$sourceZip" $tag
    if ($LASTEXITCODE -ne 0) {
        throw "Could not export tagged source."
    }
    Expand-Archive -LiteralPath $sourceZip -DestinationPath $sourceRoot -Force

    $requiredAssets = @(
        "Flare.Fireplace.Quotes.exe",
        "flare-quotes-v1-latest.json",
        "Flare.Fireplace.Quotes-portable.zip"
    )

    foreach ($asset in $requiredAssets) {
        $localPath = Join-Path $PSScriptRoot "installer\$asset"
        if (-not (Test-Path $localPath -PathType Leaf)) {
            gh release download $tag --repo $Repo --pattern $asset --dir $releaseRoot
            if ($LASTEXITCODE -ne 0) {
                throw "Could not download release asset: $asset"
            }
        }
        else {
            Copy-Item $localPath (Join-Path $releaseRoot $asset) -Force
        }
    }

    $assetRecords = foreach ($asset in $requiredAssets) {
        $path = Join-Path $releaseRoot $asset
        if (-not (Test-Path $path -PathType Leaf)) {
            throw "Backup input is missing: $asset"
        }

        [ordered]@{
            name = $asset
            sizeBytes = (Get-Item $path).Length
            sha256 = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }

    $manifest = [ordered]@{
        product = "Flare Fireplace Quotes"
        version = $Version
        tag = $tag
        commit = $commit
        createdUtc = [DateTime]::UtcNow.ToString("o")
        release = "https://github.com/$Repo/releases/tag/$tag"
        assets = @($assetRecords)
    }
    Write-Utf8NoBom (
        Join-Path $bundleRoot "BACKUP_MANIFEST.json"
    ) ($manifest | ConvertTo-Json -Depth 20)

    $readme = @"
Flare Fireplace Quotes $tag Full Backup
=======================================

This archive is the complete recoverable record for $tag.

Source\
  Exact tracked source exported from tag $tag at commit $commit.

Release\
  Matching verified installer, updater manifest, and portable application.

BACKUP_MANIFEST.json
  Version, tag, commit, release URL, byte sizes, and SHA-256 hashes.

Restore
-------
1. Preserve this ZIP unchanged as the master backup.
2. Reinstall using Release\Flare.Fireplace.Quotes.exe.
3. Rebuild from Source\ using .NET 10 SDK and Inno Setup 6.
4. Never place Gmail credentials, OAuth tokens, customer data, local settings,
   logs, reports, or generated PDFs inside this archive.
"@
    Write-Utf8NoBom (Join-Path $bundleRoot "BACKUP_README.txt") ($readme.Trim() + "`r`n")

    $forbidden = Get-ChildItem $sourceRoot -Recurse -Force |
        Where-Object {
            $_.Name -in @(".git", "bin", "obj", "TestResults") -or
            $_.Name -ieq "gmail_credentials.json" -or
            $_.Name -match '(?i)(access|refresh)[-_]?token'
        }

    if ($forbidden) {
        $forbidden | Select-Object FullName | Format-Table -AutoSize
        throw "Private or generated files were found in the backup source."
    }

    Remove-Item $outputPath -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $bundleRoot "*") `
        -DestinationPath $outputPath `
        -CompressionLevel Optimal `
        -Force

    if (-not (Test-Path $outputPath -PathType Leaf) -or
        (Get-Item $outputPath).Length -le 0) {
        throw "The full backup ZIP was not created."
    }

    gh release upload $tag "$outputPath" --repo $Repo --clobber
    if ($LASTEXITCODE -ne 0) {
        throw "The full backup could not be uploaded."
    }

    $verified = $false
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        $release = gh api "repos/$Repo/releases/tags/$tag" | ConvertFrom-Json
        $asset = @($release.assets) |
            Where-Object { $_.name -eq $assetName } |
            Select-Object -First 1

        if ($asset -and [long]$asset.size -eq (Get-Item $outputPath).Length) {
            $verified = $true
            break
        }

        Start-Sleep -Seconds 3
    }

    if (-not $verified) {
        throw "The full backup asset was not verified on the GitHub release."
    }

    Write-Host "Verified full backup: $outputPath" -ForegroundColor Green
}
finally {
    Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
