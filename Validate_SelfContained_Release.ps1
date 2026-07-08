param(
    [string]$PublishDir = "FlareQuotes.App\bin\Release\net10.0-windows\win-x64\publish"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PublishDir)) {
    throw "Publish directory not found: $PublishDir"
}

$runtimeConfig = Get-ChildItem $PublishDir -File -Filter "*.runtimeconfig.json" | Select-Object -First 1
if (-not $runtimeConfig) {
    throw "runtimeconfig.json not found."
}

$json = Get-Content $runtimeConfig.FullName -Raw | ConvertFrom-Json
$hasFrameworks = $json.runtimeOptions.PSObject.Properties.Name -contains "frameworks"

Write-Host "runtimeconfig: $($runtimeConfig.FullName)"
Write-Host "runtimeOptions.frameworks present: $hasFrameworks"

if ($hasFrameworks) {
    throw "Release is framework-dependent. Self-contained release validation failed."
}

foreach ($file in @("coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll", "PresentationFramework.dll")) {
    $path = Join-Path $PublishDir $file
    Write-Host "$file`: $(Test-Path $path)"
    if (-not (Test-Path $path)) {
        throw "Missing self-contained runtime file: $file"
    }
}

Write-Host "Self-contained release validation passed." -ForegroundColor Green