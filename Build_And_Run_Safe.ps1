$ErrorActionPreference = "Stop"

# ============================================================
# Flare Quotes - Build and Run Safe
#
# Robust version:
# - Works with net10.0-windows and win-x64 output.
# - Does not assume the EXE is directly under net10.0-windows.
# - Finds the newest Flare EXE under bin\Debug recursively.
# ============================================================

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root
[System.IO.Directory]::SetCurrentDirectory($Root)

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$log = ".\v1_build_run_$stamp.log"

Start-Transcript -Path $log -Force | Out-Null

try {
    Write-Host "Stopping running Flare Fireplaces - Quotes instances..."

    Get-Process | Where-Object {
        $_.ProcessName -like "*Flare*" -or
        $_.MainWindowTitle -like "*Flare Fireplaces - Quotes*" -or
        $_.MainWindowTitle -like "*Flare Fireplace Quotes*"
    } | Stop-Process -Force -ErrorAction SilentlyContinue

    $appCsproj = Join-Path $Root "FlareQuotes.App\FlareQuotes.App.csproj"

    if (-not (Test-Path $appCsproj)) {
        throw "App project not found: $appCsproj"
    }

    Write-Host "Restoring packages..."
    dotnet restore $appCsproj

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed."
    }

    Write-Host "Building app..."
    dotnet build $appCsproj -c Debug

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed."
    }

    $debugRoot = Join-Path $Root "FlareQuotes.App\bin\Debug"

    if (-not (Test-Path $debugRoot)) {
        throw "Debug output folder not found: $debugRoot"
    }

    $exeItem = Get-ChildItem $debugRoot -Recurse -File -Filter "*.exe" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -like "*Flare*" -and
            $_.FullName -notmatch "\\ref\\|\\refint\\|\\apphost\\"
        } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $exeItem) {
        Write-Host ""
        Write-Host "Debug output contents:"
        Get-ChildItem $debugRoot -Recurse -File -ErrorAction SilentlyContinue |
            Select-Object FullName, Length, LastWriteTime |
            Format-Table -AutoSize

        throw "EXE not found after build under: $debugRoot"
    }

    Write-Host ""
    Write-Host "Launching app:"
    Write-Host "  $($exeItem.FullName)"

    Start-Process -FilePath $exeItem.FullName

    Write-Host ""
    Write-Host "Build and launch completed."
}
catch {
    Write-Host ""
    Write-Host "ERROR:"
    Write-Host $_.Exception.Message
    Write-Host ""
    Write-Host "Debug log saved to:"
    Write-Host $log
    throw
}
finally {
    Stop-Transcript | Out-Null
}