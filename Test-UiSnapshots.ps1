param(
    [string]$OutputDirectory = ".\artifacts\ui-snapshots"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $Root $OutputDirectory))

if (Test-Path -LiteralPath $outputPath) {
    throw "UI snapshot output already exists: $outputPath"
}

New-Item -ItemType Directory -Path $outputPath | Out-Null
$env:FLARE_UI_SNAPSHOT_MODE = "1"
$env:FLARE_UI_SNAPSHOT_DIR = $outputPath

try {
    dotnet run --project (Join-Path $Root "FlareQuotes.App\FlareQuotes.App.csproj") `
        -c Release `
        --no-restore `
        -p:DefineConstants=FLARE_UI_SNAPSHOTS `
        -p:TreatWarningsAsErrors=true

    if ($LASTEXITCODE -ne 0) {
        throw "The rendered UI snapshot process failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item Env:FLARE_UI_SNAPSHOT_MODE -ErrorAction SilentlyContinue
    Remove-Item Env:FLARE_UI_SNAPSHOT_DIR -ErrorAction SilentlyContinue
}

$requiredPngFiles = @(
    "main-window-dark.png",
    "main-window-minimum.png",
    "settings-window-dark.png",
    "settings-window-minimum.png"
)

foreach ($fileName in $requiredPngFiles) {
    $path = Join-Path $outputPath $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required rendered UI artifact is missing: $fileName"
    }

    if ((Get-Item -LiteralPath $path).Length -lt 10KB) {
        throw "Rendered UI image is unexpectedly small: $fileName"
    }
}

$errorPath = Join-Path $outputPath "snapshot-error.txt"
if (Test-Path -LiteralPath $errorPath -PathType Leaf) {
    throw (Get-Content -LiteralPath $errorPath -Raw)
}

$metricsPath = Join-Path $outputPath "layout-metrics.json"
if (-not (Test-Path -LiteralPath $metricsPath -PathType Leaf)) {
    throw "Required rendered UI artifact is missing: layout-metrics.json"
}

if ((Get-Item -LiteralPath $metricsPath).Length -lt 2) {
    throw "Rendered UI metrics file is empty."
}

try {
    $metrics = Get-Content -LiteralPath $metricsPath -Raw | ConvertFrom-Json
}
catch {
    throw "Rendered UI metrics are not valid JSON: $($_.Exception.Message)"
}

if ($null -eq $metrics.mainWindow -or $null -eq $metrics.minimumMainWindow -or
    $null -eq $metrics.settingsWindow -or $null -eq $metrics.minimumSettingsWindow) {
    throw "Rendered UI metrics are incomplete."
}

if ([int]$metrics.systemHealthWindowsOpened -ne 0) {
    throw "A system-health popup appeared during normal startup."
}

Write-Host "Rendered UI snapshots and layout bounds validated successfully."
