param([switch]$VerifyOnly)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$solution = ".\FlareQuotes.sln"
if (-not (Test-Path $solution)) {
    throw "FlareQuotes.sln is missing."
}

dotnet restore $solution
if ($LASTEXITCODE -ne 0) {
    throw "Solution restore failed."
}

if ($VerifyOnly) {
    dotnet format $solution --verify-no-changes --no-restore --verbosity minimal
}
else {
    dotnet format $solution --no-restore --verbosity minimal
    if ($LASTEXITCODE -eq 0) {
        dotnet format $solution --verify-no-changes --no-restore --verbosity minimal
    }
}

if ($LASTEXITCODE -ne 0) {
    throw "Source formatting validation failed."
}

Write-Host "Source formatting is clean." -ForegroundColor Green
