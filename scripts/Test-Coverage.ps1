param(
    [ValidateRange(0, 100)]
    [int]$Threshold = 45
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$coverageDirectory = Join-Path $repoRoot 'artifacts\coverage'
$coverageFile = Join-Path $coverageDirectory 'coverage.cobertura.xml'
$testProject = Join-Path $repoRoot 'FlareQuotes.Tests\FlareQuotes.Tests.csproj'
$testAssembly = Join-Path $repoRoot 'FlareQuotes.Tests\bin\Debug\net10.0-windows\win-x64\FlareQuotes.Tests.dll'

New-Item -ItemType Directory -Path $coverageDirectory -Force | Out-Null

Push-Location $repoRoot
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to restore the repository tools.'
    }

    # Release builds intentionally omit PDBs. Debug retains portable symbols so
    # Coverlet can map executed IL back to production source lines.
    & dotnet build $testProject -c Debug --no-restore --warnaserror
    if ($LASTEXITCODE -ne 0) {
        throw 'The Debug build failed; coverage was not collected.'
    }

    $targetArguments = "test `"$testProject`" -c Debug --no-build --no-restore --verbosity minimal"
    & dotnet tool run coverlet $testAssembly `
        --target dotnet `
        --targetargs $targetArguments `
        --format cobertura `
        --output $coverageFile `
        --include '[Flare*]*' `
        --exclude '[FlareQuotes.Tests]*' `
        --exclude-by-attribute 'CompilerGeneratedAttribute,GeneratedCodeAttribute' `
        --threshold $Threshold `
        --threshold-type line `
        --threshold-stat total

    if ($LASTEXITCODE -ne 0) {
        throw "Production line coverage is below the $Threshold% baseline, or the coverage run failed."
    }
}
finally {
    Pop-Location
}

Write-Host "Coverage gate passed. Report: $coverageFile"
