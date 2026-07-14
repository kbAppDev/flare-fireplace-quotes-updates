param(
    [switch]$SkipGmailIntegration,
    [switch]$QueueCodeQL
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$reportDir = Join-Path $env:LOCALAPPDATA "Flare Fireplace Quotes\Reports"
New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$log = Join-Path $reportDir "release-gate-$timestamp.log"
$summaryPath = Join-Path $reportDir "release-gate-summary-$timestamp.txt"

function Assert-NativeSuccess([int]$ExitCode, [string]$Message) {
    if ($ExitCode -ne 0) {
        throw $Message
    }
}

Start-Transcript -Path $log -Force
try {
    Write-Host "1/8 Formatting source and verifying a clean format..."
    dotnet format .\FlareQuotes.App\FlareQuotes.App.csproj --verbosity minimal
    Assert-NativeSuccess $LASTEXITCODE "App source formatting failed."
    dotnet format .\FlareQuotes.Tests\FlareQuotes.Tests.csproj --verbosity minimal
    Assert-NativeSuccess $LASTEXITCODE "Test source formatting failed."
    dotnet format .\FlareQuotes.App\FlareQuotes.App.csproj --verify-no-changes --verbosity minimal
    Assert-NativeSuccess $LASTEXITCODE "App source is not format-clean."
    dotnet format .\FlareQuotes.Tests\FlareQuotes.Tests.csproj --verify-no-changes --verbosity minimal
    Assert-NativeSuccess $LASTEXITCODE "Test source is not format-clean."

    Write-Host "2/8 Clean Release build with every warning treated as an error..."
    dotnet clean .\FlareQuotes.App\FlareQuotes.App.csproj -c Release
    Assert-NativeSuccess $LASTEXITCODE "App clean failed."
    dotnet clean .\FlareQuotes.Tests\FlareQuotes.Tests.csproj -c Release
    Assert-NativeSuccess $LASTEXITCODE "Test clean failed."
    dotnet build .\FlareQuotes.App\FlareQuotes.App.csproj -c Release -p:TreatWarningsAsErrors=true
    Assert-NativeSuccess $LASTEXITCODE "App Release build failed."
    dotnet build .\FlareQuotes.Tests\FlareQuotes.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
    Assert-NativeSuccess $LASTEXITCODE "Test Release build failed."

    Write-Host "3/8 Unit and every-model payload regression tests..."
    dotnet test .\FlareQuotes.Tests\FlareQuotes.Tests.csproj `
        -c Release `
        --no-build `
        --filter "FullyQualifiedName!~GmailEveryModelIntegrationTests" `
        --logger "trx;LogFileName=unit-tests.trx"
    Assert-NativeSuccess $LASTEXITCODE "Unit or regression tests failed."

    $trx = Get-ChildItem .\FlareQuotes.Tests\TestResults -Recurse -Filter "unit-tests.trx" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($trx) {
        Copy-Item $trx.FullName (Join-Path $reportDir "unit-tests-$timestamp.trx") -Force
    }

    Write-Host "4/8 NuGet dependency vulnerability audit..."
    $appAuditPath = Join-Path $reportDir "vulnerability-audit-app-$timestamp.json"
    $testAuditPath = Join-Path $reportDir "vulnerability-audit-tests-$timestamp.json"

    dotnet list .\FlareQuotes.App\FlareQuotes.App.csproj package --vulnerable --include-transitive --format json |
        Out-File -FilePath $appAuditPath -Encoding utf8
    Assert-NativeSuccess $LASTEXITCODE "App dependency audit failed to run."

    dotnet list .\FlareQuotes.Tests\FlareQuotes.Tests.csproj package --vulnerable --include-transitive --format json |
        Out-File -FilePath $testAuditPath -Encoding utf8
    Assert-NativeSuccess $LASTEXITCODE "Test dependency audit failed to run."

    $vulnerabilityHits = Select-String `
        -Path $appAuditPath, $testAuditPath `
        -Pattern '"advisoryUrl"\s*:' `
        -CaseSensitive:$false
    if ($vulnerabilityHits) {
        throw "One or more vulnerable NuGet packages were reported. Review the vulnerability audit files."
    }

    Write-Host "5/8 Security, credential-package, and removed-image-lookup scans..."
    $sourceFiles = Get-ChildItem . -Recurse -File -Include *.cs, *.xaml, *.json
    $forbiddenImageLookup = $sourceFiles | Select-String -SimpleMatch -Pattern `
        "wp-json/wp/v2/media", `
        "GalleryPhotoAttachmentService", `
        "DownloadGallery"
    if ($forbiddenImageLookup) {
        $forbiddenImageLookup | Format-Table Path, LineNumber, Line -AutoSize
        throw "Automatic online image lookup code remains."
    }

    $packagedCredentials = Get-ChildItem . -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -ieq "gmail_credentials.json" -or
            $_.Name -match "(?i)(access|refresh)[-_]?token.*\.json$"
        }
    if ($packagedCredentials) {
        $packagedCredentials | Select-Object FullName | Format-Table -AutoSize
        throw "Credential or token files are packaged with the source."
    }

    $embeddedSecrets = Get-ChildItem . -Recurse -File -Include *.json, *.cs, *.ps1 |
        Select-String -Pattern '"client_secret"\s*:\s*"(?!REDACTED|PLACEHOLDER)' -CaseSensitive:$false
    if ($embeddedSecrets) {
        $embeddedSecrets | Format-Table Path, LineNumber, Line -AutoSize
        throw "A possible embedded client secret was found."
    }

    $requiredHardeningFiles = @(
        ".\FlareQuotes.Core\Security\ProtectedJsonFileStore.cs",
        ".\FlareQuotes.Core\Updates\UpdateTrustPolicy.cs",
        ".\FlareQuotes.Tests\SecurityTests\ProtectedJsonFileStoreTests.cs",
        ".\FlareQuotes.Tests\UpdateTests\UpdateTrustPolicyTests.cs"
    )
    foreach ($requiredFile in $requiredHardeningFiles) {
        if (-not (Test-Path $requiredFile)) {
            throw "Required deployment-hardening file is missing: $requiredFile"
        }
    }

    $mainViewModelSource = Get-Content ".\FlareQuotes.App\ViewModels\MainViewModel.cs" -Raw
    if ($mainViewModelSource -match 'File\.WriteAllText\s*\(\s*RecallHistoryPath') {
        throw "Recall quote history still contains a plaintext write path."
    }
    if ($mainViewModelSource -notmatch 'DeleteGeneratedPdfAfterSuccessfulDraft') {
        throw "Post-draft temporary PDF cleanup is missing."
    }

    $releaseScriptSource = Get-Content ".\Build_Release_Installer.ps1" -Raw
    if ($releaseScriptSource -notmatch 'sizeBytes' -or
        $releaseScriptSource -notmatch 'releases/download/\$Tag/\$InstallerAssetName') {
        throw "Release manifest generation is missing version-specific URL or size verification data."
    }

    $gmailStatus = "SKIPPED"
    if (-not $SkipGmailIntegration) {
        Write-Host "6/8 Live Gmail create-confirm-delete test for every fireplace model..."
        Write-Host "Progress file: $reportDir\gmail-every-model-progress.log"
        Remove-Item (Join-Path $reportDir "gmail-every-model-report.csv") -Force -ErrorAction SilentlyContinue
        Remove-Item (Join-Path $reportDir "gmail-category-summary.csv") -Force -ErrorAction SilentlyContinue
        Remove-Item (Join-Path $reportDir "gmail-every-model-progress.log") -Force -ErrorAction SilentlyContinue

        $env:FLARE_RUN_GMAIL_INTEGRATION = "1"
        dotnet test .\FlareQuotes.Tests\FlareQuotes.Tests.csproj `
            -c Release `
            --no-build `
            --filter "FullyQualifiedName~GmailEveryModelIntegrationTests" `
            --logger "console;verbosity=normal"
        Assert-NativeSuccess $LASTEXITCODE "Live Gmail integration test failed."
        $gmailStatus = "PASS"
    }
    else {
        Write-Warning "Live Gmail integration testing was skipped. This is not a complete release validation."
    }

    Write-Host "7/8 Validating and summarizing every-model reports..."
    $gmailReportPath = Join-Path $reportDir "gmail-every-model-report.csv"
    $modelCount = 0
    $categoryCount = 0

    if (-not $SkipGmailIntegration) {
        if (-not (Test-Path $gmailReportPath)) {
            throw "The live Gmail model report was not generated."
        }

        $gmailRows = @(Import-Csv $gmailReportPath)
        if ($gmailRows.Count -eq 0) {
            throw "The live Gmail model report contains no model rows."
        }

        $failedRows = @($gmailRows | Where-Object {
            $_.Result -ne "PASS" -or
            $_.DraftConfirmed -ne "true" -or
            $_.Deleted -ne "true"
        })
        if ($failedRows.Count -gt 0) {
            $failedRows | Format-Table Model, Category, Result, DraftConfirmed, Deleted, Error -AutoSize
            throw "$($failedRows.Count) live Gmail model test(s) did not pass cleanly."
        }

        $inventoryPath = Join-Path $PSScriptRoot "LocalData\expected-fireplace-model-inventory.csv"
        if (-not (Test-Path $inventoryPath)) {
            throw "Expected fireplace-model inventory is missing: $inventoryPath"
        }

        $inventoryRows = @(Import-Csv $inventoryPath)
        if ($inventoryRows.Count -ne 302) {
            throw "Expected 302 fireplace models in the canonical inventory, but found $($inventoryRows.Count)."
        }
        $expectedModels = @($inventoryRows | Select-Object -ExpandProperty Model | Sort-Object -Unique)
        $reportedModels = @($gmailRows | Select-Object -ExpandProperty Model | Sort-Object -Unique)
        $missingModels = @($expectedModels | Where-Object { $_ -notin $reportedModels })
        $unexpectedModels = @($reportedModels | Where-Object { $_ -notin $expectedModels })
        if ($missingModels.Count -gt 0 -or $unexpectedModels.Count -gt 0) {
            Write-Host "Missing models: $($missingModels -join ', ')"
            Write-Host "Unexpected models: $($unexpectedModels -join ', ')"
            throw "The Gmail report does not match the canonical fireplace-model inventory."
        }

        $modelCount = $gmailRows.Count
        $categoryCount = @($gmailRows | Select-Object -ExpandProperty Category -Unique).Count
        if ($categoryCount -ne 6) {
            throw "Expected six fireplace categories, but the Gmail report contains $categoryCount."
        }
    }

    Write-Host "8/8 CodeQL status..."
    $codeQlStatus = "PENDING"
    if ($QueueCodeQL -and (Get-Command gh -ErrorAction SilentlyContinue) -and (Test-Path .git)) {
        gh workflow run codeql.yml
        Assert-NativeSuccess $LASTEXITCODE "CodeQL workflow could not be queued."
        $codeQlStatus = "QUEUED"
        Write-Host "CodeQL workflow queued. Release remains pending until the workflow passes."
    }
    else {
        Write-Warning "CodeQL requires the committed GitHub repository. The included workflow must pass before publishing."
    }

    $summary = @(
        "Flare Fireplace Quotes Release Gate",
        "Timestamp: $(Get-Date -Format o)",
        "Format: PASS",
        "Warnings-as-errors build: PASS",
        "Unit/regression tests: PASS",
        "NuGet vulnerability audit: PASS",
        "Security/source scan: PASS",
        "Live Gmail every-model test: $gmailStatus",
        "Models confirmed and deleted: $modelCount",
        "Categories covered: $categoryCount",
        "CodeQL: $codeQlStatus",
        "Local gate log: $log",
        "Every-model report: $gmailReportPath"
    )
    Set-Content -Path $summaryPath -Value $summary -Encoding utf8

    Write-Host ""
    Write-Host "PASS: Local release gates passed."
    Write-Host "Models confirmed and deleted: $modelCount"
    Write-Host "Reports: $reportDir"
    Write-Host "CodeQL: $codeQlStatus - publishing remains blocked until CodeQL passes."
}
finally {
    Remove-Item Env:FLARE_RUN_GMAIL_INTEGRATION -ErrorAction SilentlyContinue
    Stop-Transcript
}
