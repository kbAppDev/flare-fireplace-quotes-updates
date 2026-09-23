[CmdletBinding()]
param(
    [string]$OutputPath = "artifacts/ui-snapshots",
    [ValidateRange(20, 300)]
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$resolvedOutputPath = if ([System.IO.Path]::IsPathFullyQualified($OutputPath)) {
    [System.IO.Path]::GetFullPath($OutputPath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
}

if (Test-Path -LiteralPath $resolvedOutputPath) {
    throw "UI snapshot output already exists: $resolvedOutputPath"
}

New-Item -ItemType Directory -Path $resolvedOutputPath | Out-Null
$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$snapshotAppData = Join-Path $temporaryRoot ("flare-ui-snapshot-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $snapshotAppData | Out-Null

try {
    Push-Location $repositoryRoot
    try {
        dotnet build .\FlareQuotes.App\FlareQuotes.App.csproj `
            -c Release `
            --no-restore `
            -p:DefineConstants=FLARE_UI_SNAPSHOTS `
            -p:TreatWarningsAsErrors=true `
            -p:ContinuousIntegrationBuild=true
        if ($LASTEXITCODE -ne 0) {
            throw "The snapshot-enabled application build failed."
        }

        $appExecutable = Join-Path $repositoryRoot `
            "FlareQuotes.App\bin\Release\net10.0-windows\win-x64\Flare Fireplace Quotes.exe"
        if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) {
            throw "The snapshot executable was not produced at the expected path."
        }

        $env:FLARE_UI_SNAPSHOT_MODE = "1"
        $env:FLARE_UI_SNAPSHOT_DIR = $resolvedOutputPath
        $env:FLARE_UI_SNAPSHOT_APPDATA = $snapshotAppData
        $env:FLARE_UI_SNAPSHOT_TIMEOUT_SECONDS = [string]$TimeoutSeconds

        $process = Start-Process -FilePath $appExecutable -PassThru -WindowStyle Hidden
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try {
                $process.Kill($true)
                $process.WaitForExit()
            } catch {
                # Preserve the original timeout as the actionable failure.
            }

            $timeoutMessage = "UI snapshot process exceeded the $TimeoutSeconds-second runner timeout and was terminated."
            Set-Content -LiteralPath (Join-Path $resolvedOutputPath "snapshot-runner-error.txt") `
                -Value $timeoutMessage -Encoding utf8NoBOM
            throw $timeoutMessage
        }

        if (Test-Path -LiteralPath (Join-Path $resolvedOutputPath "snapshot-error.txt")) {
            throw (Get-Content -LiteralPath (Join-Path $resolvedOutputPath "snapshot-error.txt") -Raw)
        }
        if ($process.ExitCode -ne 0) {
            throw "UI snapshot process exited with code $($process.ExitCode)."
        }

        $requiredFiles = @(
            "main-window-dark.png",
            "main-window-minimum.png",
            "settings-window-dark.png",
            "settings-window-minimum.png",
            "layout-metrics.json"
        )
        foreach ($fileName in $requiredFiles) {
            $path = Join-Path $resolvedOutputPath $fileName
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Required UI snapshot artifact is missing: $fileName"
            }
        }

        $pngSignature = "89504E470D0A1A0A"
        foreach ($fileName in $requiredFiles.Where({ $_.EndsWith(".png", [StringComparison]::Ordinal) })) {
            $path = Join-Path $resolvedOutputPath $fileName
            $bytes = [System.IO.File]::ReadAllBytes($path)
            if ($bytes.Length -lt 4096 -or
                [Convert]::ToHexString($bytes[0..7]) -ne $pngSignature) {
                throw "UI snapshot artifact is not a usable PNG: $fileName"
            }
        }

        $metricsPath = Join-Path $resolvedOutputPath "layout-metrics.json"
        $metrics = Get-Content -LiteralPath $metricsPath -Raw | ConvertFrom-Json
        if ([int]$metrics.mainWindow.representativeQuantity -ne 3 -or
            $metrics.mainWindow.passiveHeatFlexSelected -ne $true -or
            [int]$metrics.mainWindow.passiveHeatFlexChipCount -lt 2 -or
            [int]$metrics.mainWindow.savedQuantityLabelCount -lt 1) {
            throw "UI snapshots did not prove the quantity and Passive Heat Flex visual contract."
        }

        Write-Host "UI snapshot gate passed. Artifacts: $resolvedOutputPath"
    } finally {
        Pop-Location
    }
} catch {
    $runnerErrorPath = Join-Path $resolvedOutputPath "snapshot-runner-error.txt"
    if (-not (Test-Path -LiteralPath $runnerErrorPath)) {
        Set-Content -LiteralPath $runnerErrorPath -Value ($_ | Out-String) -Encoding utf8NoBOM
    }
    throw
} finally {
    $resolvedAppData = [System.IO.Path]::GetFullPath($snapshotAppData)
    $temporaryPrefix = $temporaryRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    if ($resolvedAppData.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        [System.IO.Path]::GetFileName($resolvedAppData).StartsWith("flare-ui-snapshot-", [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedAppData -Recurse -Force -ErrorAction SilentlyContinue
    }
}
