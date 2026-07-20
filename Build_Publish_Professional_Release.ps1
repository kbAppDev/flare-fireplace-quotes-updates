param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [string]$Repo = "kbAppDev/flare-fireplace-quotes-updates",

    [string]$ReleaseNotes = "",

    [string]$CertificateThumbprint = "",

    [string]$PfxPath = "",

    [string]$PfxPassword = "",

    [string]$TimestampUrl = "https://timestamp.digicert.com",

    [string]$ManifestSigningPrivateKeyPath = ""
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

$AssetName = "Flare.Fireplace.Quotes.exe"
$ManifestName = "flare-quotes-v1-latest.json"
$InstallerDir = Join-Path $Root "installer"
$AssetPath = Join-Path $InstallerDir $AssetName
$ManifestPath = Join-Path $InstallerDir $ManifestName
$Tag = "v$Version"
$ReleaseAssetUrl = "https://github.com/$Repo/releases/download/$Tag/$AssetName"
$LatestManifestUrl = "https://github.com/$Repo/releases/latest/download/$ManifestName"

if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
    $ReleaseNotes = "Flare Fireplace Quotes $Tag professional release."
}

function Require-Command($Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $Name"
    }
}

function Write-Utf8NoBom($Path, $Text) {
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $enc)
}

function Find-Iscc {
    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd -and (Test-Path $cmd.Source)) { return $cmd.Source }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }

    return $null
}

function Invoke-CodeSign($Path) {
    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint) -and [string]::IsNullOrWhiteSpace($PfxPath)) {
        Write-Host "Code signing skipped. Pass -CertificateThumbprint or -PfxPath when the certificate is ready." -ForegroundColor Yellow
        return
    }

    $signtool = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if (-not $signtool) {
        throw "signtool.exe was not found. Install Windows SDK to code-sign releases."
    }

    if (-not [string]::IsNullOrWhiteSpace($PfxPath)) {
        if (-not (Test-Path $PfxPath)) { throw "PFX not found: $PfxPath" }
        & $signtool.Source sign /fd SHA256 /tr $TimestampUrl /td SHA256 /f $PfxPath /p $PfxPassword $Path
    }
    else {
        & $signtool.Source sign /fd SHA256 /tr $TimestampUrl /td SHA256 /sha1 $CertificateThumbprint $Path
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Code signing failed for $Path"
    }

    & $signtool.Source verify /pa /v $Path
    if ($LASTEXITCODE -ne 0) {
        throw "Code-signature verification failed for $Path"
    }
}

function Assert-SelfContained($PublishDir) {
    $runtimeConfig = Get-ChildItem $PublishDir -File -Filter "*.runtimeconfig.json" | Select-Object -First 1
    if (-not $runtimeConfig) { throw "runtimeconfig.json not found in publish output." }

    $json = Get-Content $runtimeConfig.FullName -Raw | ConvertFrom-Json
    $hasFrameworks = $json.runtimeOptions.PSObject.Properties.Name -contains "frameworks"

    if ($hasFrameworks) {
        throw "Release is framework-dependent. runtimeOptions.frameworks must not exist in a self-contained release."
    }

    foreach ($file in @("coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll", "PresentationFramework.dll")) {
        if (-not (Test-Path (Join-Path $PublishDir $file))) {
            throw "Self-contained runtime file missing: $file"
        }
    }
}

function New-ManifestSignature($Manifest) {
    if ([string]::IsNullOrWhiteSpace($ManifestSigningPrivateKeyPath)) {
        return ""
    }

    if (-not (Test-Path $ManifestSigningPrivateKeyPath)) {
        throw "Manifest signing private key was not found: $ManifestSigningPrivateKeyPath"
    }

    $privateKeyPem = Get-Content $ManifestSigningPrivateKeyPath -Raw
    $rsa = [System.Security.Cryptography.RSA]::Create()
    $rsa.ImportFromPem($privateKeyPem)

    $payload = "$($Manifest.version)`n$($Manifest.url)`n$($Manifest.sha256)`n$($Manifest.sizeBytes)`n$($Manifest.notes)"
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $sig = $rsa.SignData($bytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    return [Convert]::ToBase64String($sig)
}

Require-Command "dotnet"
Require-Command "gh"

gh auth status | Out-Host
if ($LASTEXITCODE -ne 0) { throw "GitHub CLI is not authenticated." }

gh release view $Tag --repo $Repo *> $null
if ($LASTEXITCODE -eq 0) { throw "GitHub release $Tag already exists. Use a higher version." }

$versionParts = $Version.Split(".")
while ($versionParts.Count -lt 3) { $versionParts += "0" }
$Version4 = "$($versionParts[0]).$($versionParts[1]).$($versionParts[2]).0"

Get-Process | Where-Object {
    $_.ProcessName -like "*Flare*" -or
    $_.MainWindowTitle -like "*Flare Fireplace Quotes*"
} | Stop-Process -Force -ErrorAction SilentlyContinue

Get-ChildItem $Root -Recurse -Directory -Force | Where-Object { $_.Name -in @("bin","obj") } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $InstallerDir -Force | Out-Null
Get-ChildItem $InstallerDir -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in @(".exe",".json",".log") } | Remove-Item -Force

$AppProject = Join-Path $Root "FlareQuotes.App\FlareQuotes.App.csproj"
dotnet restore $AppProject -r win-x64
dotnet publish $AppProject -c Release -r win-x64 --self-contained true -p:SelfContained=true -p:PublishSingleFile=false -p:Version=$Version -p:AssemblyVersion=$Version4 -p:FileVersion=$Version4 -p:InformationalVersion=$Version -p:ProductVersion=$Version4

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$publishExe = Get-ChildItem (Join-Path $Root "FlareQuotes.App\bin\Release") -Recurse -File -Filter "*.exe" |
    Where-Object { $_.DirectoryName -match "\\publish$" -and $_.Name -like "*Flare*" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $publishExe) { throw "Published app EXE not found." }

Assert-SelfContained $publishExe.DirectoryName

$iscc = Find-Iscc
if (-not $iscc) { throw "ISCC.exe not found. Install Inno Setup 6." }

$iss = Get-ChildItem $Root -Recurse -File -Filter "*.iss" | Sort-Object FullName | Select-Object -First 1
if (-not $iss) { throw "No Inno Setup .iss file found." }

& $iscc "/DMyAppVersion=$Version" "/DSourceDir=$($publishExe.DirectoryName)" $iss.FullName
if ($LASTEXITCODE -ne 0) { throw "Inno Setup build failed." }

$installer = Get-ChildItem $InstallerDir -Recurse -File -Filter "*.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $installer) { throw "Installer EXE not found." }

$installerSource = [System.IO.Path]::GetFullPath($installer.FullName)
$installerDest = [System.IO.Path]::GetFullPath($AssetPath)

if ($installerSource -ne $installerDest) {
    Copy-Item $installerSource $installerDest -Force
}
else {
    Write-Host "Installer already exists at target path; skipping copy."
}

# Keep only the release asset name.
Get-ChildItem $InstallerDir -File -Filter "*.exe" |
    Where-Object { $_.FullName -ne $AssetPath } |
    Remove-Item -Force

Invoke-CodeSign $AssetPath

$hash = (Get-FileHash -Algorithm SHA256 -Path $AssetPath).Hash.ToLowerInvariant()
$size = (Get-Item $AssetPath).Length

$manifest = [ordered]@{
    version = $Version
    url = $ReleaseAssetUrl
    installer = $ReleaseAssetUrl
    sha256 = $hash
    sizeBytes = $size
    notes = $ReleaseNotes.Trim()
}

$manifestSignature = New-ManifestSignature $manifest
if (-not [string]::IsNullOrWhiteSpace($manifestSignature)) {
    $manifest.signatureAlgorithm = "RS256"
    $manifest.signature = $manifestSignature
}
Write-Utf8NoBom $ManifestPath ($manifest | ConvertTo-Json -Depth 20)

gh release create $Tag "$AssetPath" "$ManifestPath" --repo $Repo --title $Tag --notes $ReleaseNotes
if ($LASTEXITCODE -ne 0) { throw "GitHub release create failed." }

Write-Host "Verifying live updater assets..." -ForegroundColor Cyan
$live = $null
for ($attempt = 1; $attempt -le 18; $attempt++) {
    try {
        $manifestUriBuilder = [System.UriBuilder]::new($LatestManifestUrl)
        $manifestUriBuilder.Query = "cacheBust=$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())"
        $live = Invoke-RestMethod -Uri $manifestUriBuilder.Uri `
                                  -Headers @{ "Cache-Control" = "no-cache" }
        if ([string]$live.version -eq $Version) { break }
    }
    catch {
        if ($attempt -eq 18) { throw }
    }

    if ($attempt -lt 18) { Start-Sleep -Seconds 5 }
}

if ([string]$live.version -ne $Version -or
    [string]$live.url -ne $ReleaseAssetUrl -or
    [string]$live.installer -ne $ReleaseAssetUrl -or
    [string]$live.sha256 -ne $hash -or
    [long]$live.sizeBytes -ne $size) {
    throw "Live manifest does not exactly match the published $Tag installer metadata."
}

$verificationDownload = Join-Path ([System.IO.Path]::GetTempPath()) "Flare.Fireplace.Quotes.$([Guid]::NewGuid().ToString('N')).exe"
try {
    Invoke-WebRequest -Uri $ReleaseAssetUrl -OutFile $verificationDownload -Headers @{ "Cache-Control" = "no-cache" }
    $liveSize = (Get-Item $verificationDownload).Length
    $liveHash = (Get-FileHash -Algorithm SHA256 -Path $verificationDownload).Hash.ToLowerInvariant()
    if ($liveSize -ne $size -or $liveHash -ne $hash) {
        throw "Downloaded GitHub release asset does not match the locally verified installer."
    }
}
finally {
    Remove-Item $verificationDownload -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "DONE - $Tag professional release published" -ForegroundColor Green
