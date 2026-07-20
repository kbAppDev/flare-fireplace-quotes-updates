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

$requiredFiles = @(
    "main-window-dark.png",
    "main-window-minimum.png",
    "settings-window-dark.png",
    "settings-window-minimum.png",
    "layout-metrics.json"
)

foreach ($fileName in $requiredFiles) {
    $path = Join-Path $outputPath $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required rendered UI artifact is missing: $fileName"
    }

    if ((Get-Item -LiteralPath $path).Length -lt 1KB) {
        throw "Rendered UI artifact is unexpectedly small: $fileName"
    }
}

$errorPath = Join-Path $outputPath "snapshot-error.txt"
if (Test-Path -LiteralPath $errorPath -PathType Leaf) {
    throw (Get-Content -LiteralPath $errorPath -Raw)
}

$metrics = Get-Content -LiteralPath (Join-Path $outputPath "layout-metrics.json") -Raw | ConvertFrom-Json
if ([int]$metrics.systemHealthWindowsOpened -ne 0) {
    throw "A system-health popup appeared during normal startup."
}

Write-Host "Rendered UI snapshots and layout bounds validated successfully."
