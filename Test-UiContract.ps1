param()

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Read-ValidatedXaml([string]$RelativePath) {
    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required XAML file is missing: $RelativePath"
    }

    $content = Get-Content -LiteralPath $path -Raw
    try {
        $document = [xml]$content
        $null = $document.DocumentElement
    }
    catch {
        throw "XAML is not well-formed: $RelativePath. $($_.Exception.Message)"
    }

    return $content
}

function Assert-ContainsAll([string]$Content, [string]$RelativePath, [string[]]$RequiredTokens) {
    foreach ($token in $RequiredTokens) {
        if (-not $Content.Contains($token, [StringComparison]::Ordinal)) {
            throw "UI contract token '$token' is missing from $RelativePath."
        }
    }
}

$mainPath = "FlareQuotes.App\Views\MainWindow.xaml"
$main = Read-ValidatedXaml $mainPath
Assert-ContainsAll $main $mainPath @(
    'x:Name="HeaderLogoImage"',
    'x:Name="AppVersionText"',
    'x:Name="ThemeToggleButton"',
    'x:Name="RecallLastQuoteButton"',
    'x:Name="PdfPreviewHost"',
    'x:Name="PdfPreviewFallback"',
    'x:Name="FeatureDropdownPopup"',
    'x:Name="ClassicMediaDropdownPopup"',
    'x:Name="AdditionalClassicMediaDropdownPopup"',
    'x:Name="PremiumMediaDropdownPopup"',
    'x:Name="LeadTimeDropdownPopup"',
    'RawRequest',
    'WorkflowStage',
    'ProjectName',
    'ClientName',
    'InstallDate',
    'FireplaceLocation',
    'GeneratedPdfSummary',
    'AutoFillCommand',
    'ClearCommand',
    'RecallQuoteCommand',
    'NextToPreviewCommand',
    'BackToReviewCommand',
    'OpenGeneratedPdfCommand',
    'NextToSpecLinksCommand',
    'BackToPreviewCommand',
    'CreateDraftCommand',
    'AddFireplaceCommand',
    'RemoveFireplaceCommand',
    'SettingsButton_Click',
    'ThemeToggleButton_Checked',
    'ThemeToggleButton_Unchecked'
)

$settingsPath = "FlareQuotes.App\Views\SettingsWindow.xaml"
$settings = Read-ValidatedXaml $settingsPath
Assert-ContainsAll $settings $settingsPath @(
    'x:Name="SalesEmailBox"',
    'x:Name="SalesPhoneBox"',
    'x:Name="WebsiteBox"',
    'x:Name="ConsultationUrlBox"',
    'x:Name="HubSpotBccBox"',
    'x:Name="GmailCredentialsPathBox"',
    'x:Name="PricingFileBox"',
    'x:Name="UpdateManifestUrlBox"',
    'x:Name="UseGmailSignatureCheckBox"',
    'x:Name="CheckUpdatesOnStartupCheckBox"',
    'x:Name="RecallQuoteHistoryLimitBox"',
    'x:Name="LeadTimePresetsBox"',
    'x:Name="SettingsStatusText"',
    'BrowseGmailCredentials_Click',
    'BrowsePricingFile_Click',
    'SaveButton_Click'
)

$updatePath = "FlareQuotes.App\Views\UpdateAvailableWindow.xaml"
$update = Read-ValidatedXaml $updatePath
Assert-ContainsAll $update $updatePath @(
    'x:Name="VersionLineText"',
    'x:Name="VersionBadgeText"',
    'x:Name="ReleaseNotesText"',
    'x:Name="LaterButton"',
    'x:Name="InstallButton"',
    'LaterButton_Click',
    'InstallButton_Click'
)

$healthPath = "FlareQuotes.App\Views\SystemHealthWindow.xaml"
$health = Read-ValidatedXaml $healthPath
Assert-ContainsAll $health $healthPath @(
    'ItemsSource="{Binding Items}"',
    'StateText',
    'OpenLogsButton_Click',
    'CloseButton_Click'
)

$themePath = "FlareQuotes.App\Themes\FlareTheme.xaml"
$theme = Read-ValidatedXaml $themePath
Assert-ContainsAll $theme $themePath @(
    'x:Key="WindowSurfaceBrush"',
    'x:Key="WindowTitleBarBrush"',
    'x:Key="WindowWorkspaceBrush"',
    'x:Key="SurfaceBrush"',
    'x:Key="PrimaryButton"',
    'x:Key="QuietButton"',
    'x:Key="CaptionButton"',
    'x:Key="CloseCaptionButton"',
    'x:Key="ChipRemoveButton"'
)

Write-Host "UI contract validated successfully."
