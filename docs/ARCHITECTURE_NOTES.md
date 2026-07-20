# Architecture Notes

Flare Fireplace Quotes v1.5.1 is a C# / WPF / MVVM rebuild of the original Python quote application.

## Project layout

- `FlareQuotes.App`: WPF views, view models, app startup, theme assets, and UI behavior.
- `FlareQuotes.Core`: business models, parsing, feature/media catalogs, email templates, interfaces, settings contracts, DPAPI JSON protection, and the fixed update trust policy.
- `FlareQuotes.Infrastructure`: Excel pricing/resource lookup, QuestPDF generation, Gmail draft creation, update checks, and DPAPI token storage.
- `FlareQuotes.Tests`: xUnit tests for parser, feature selection, all-model email payloads, Gmail integration, encrypted history, and update trust policy.
- `LocalData`: company pricing workbook and resource links workbook. Gmail credentials are supplied locally and are not included in source packages.

## Key service boundaries

- Parsing: `IQuoteRequestParser`
- Pricing: `IPriceBookService`
- Feature selection: `IFeatureSelectionService`
- Media selection: `IMediaSelectionService`
- PDF generation: `IQuotePdfService`
- Gmail draft transport: `IGmailDraftService`
- Gmail draft orchestration: `DraftWorkflowService`
- Settings: `ISettingsService`
- Updates: `IUpdateService`

## Startup rule

MainWindow must render before any heavy preview, Gmail, PDF, pricing, update, or settings operations run. A prior build issue came from blocking the WPF window during startup. Keep all heavy work lazy and recoverable.

## PDF preview rule

Live Preview uses WebView2 lazily after a PDF exists. It must never be created in XAML at startup. Open Generated PDF must remain available as the stable fallback.

## UI contract

The v1.5 presentation layer uses shared dynamic resources in `FlareTheme.xaml`; views should not introduce standalone theme palettes. `Test-UiContract.ps1` protects the named controls, bindings, commands, handlers, and updater surfaces required by code-behind and the quote workflow. Run it before every build that changes XAML.
