$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root
[System.IO.Directory]::SetCurrentDirectory($Root)

Write-Host "Stopping running Flare Fireplace Quotes instances..." -ForegroundColor Cyan
Get-Process | Where-Object {
    $_.ProcessName -like "*Flare*" -or
    $_.MainWindowTitle -like "*Flare Fireplace Quotes*"
} | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "Restoring packages..." -ForegroundColor Cyan
dotnet restore .\FlareQuotes.App\FlareQuotes.App.csproj

Write-Host "Building Debug..." -ForegroundColor Cyan
dotnet build .\FlareQuotes.App\FlareQuotes.App.csproj -c Debug --no-restore

if (Test-Path .\FlareQuotes.Tests\FlareQuotes.Tests.csproj) {
    Write-Host "Running tests..." -ForegroundColor Cyan
    dotnet test .\FlareQuotes.Tests\FlareQuotes.Tests.csproj -c Debug --no-restore
}

Write-Host "Build complete." -ForegroundColor Green
Write-Host "Launch with:" -ForegroundColor Yellow
Write-Host ".\Build_And_Run_Safe.ps1" -ForegroundColor Yellow

Write-Host ""
Read-Host "Press Enter to close"
