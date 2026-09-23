using System.IO;
using System.Windows;
using System.Windows.Threading;
using FlareQuotes.App.ViewModels;
using FlareQuotes.App.Services;
using FlareQuotes.Core.Email;
using FlareQuotes.Core.Features;
using FlareQuotes.Core.Media;
using FlareQuotes.Core.Paths;
using FlareQuotes.Core.Parsing;
using FlareQuotes.Core.Security;
using FlareQuotes.Core.Services;
using FlareQuotes.Core.Settings;
using FlareQuotes.Infrastructure.Excel;
using FlareQuotes.Infrastructure.Gmail;
using FlareQuotes.Infrastructure.Health;
using FlareQuotes.Infrastructure.Logging;
using FlareQuotes.Infrastructure.Pdf;
using FlareQuotes.Infrastructure.Security;
using FlareQuotes.Infrastructure.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace FlareQuotes.App;

public partial class App : Application
{
    public static ServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.MigrateLegacyData();
        var collection = new ServiceCollection();
        collection.AddSingleton<IAppLogger, RedactingFileLogger>();
        collection.AddSingleton<ISecurityAuditService, SecurityAuditService>();
        collection.AddSingleton<ISystemHealthService, SystemHealthService>();
        collection.AddSingleton<IQuoteRequestParser, DefaultQuoteRequestParser>();
        collection.AddSingleton<IFeatureSelectionService, FeatureSelectionService>();
        collection.AddSingleton<IMediaSelectionService, MediaSelectionService>();
        collection.AddSingleton<IPriceBookService, ClosedXmlPriceBookService>();
        collection.AddSingleton<IQuotePdfService, QuestPdfQuotePdfService>();
        collection.AddSingleton<IGmailDraftService, GmailDraftService>();
        collection.AddSingleton<ISettingsService>(provider =>
            new JsonSettingsService(provider.GetRequiredService<IAppLogger>()));
        collection.AddSingleton<IUpdateService, HttpUpdateService>();
        collection.AddSingleton<EmailTemplateService>();
        collection.AddSingleton<DraftWorkflowService>();
        collection.AddTransient<MainViewModel>();

        Services = collection.BuildServiceProvider();

        InstallGlobalExceptionHandling();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services?.Dispose();
        base.OnExit(e);
    }

    private void InstallGlobalExceptionHandling()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            var logger = Services.GetService<IAppLogger>();
            logger?.Error(args.Exception, "Unhandled UI exception.");

            var canContinue = IsRecoverableUiException(args.Exception);
            var fallback = canContinue
                               ? "A recoverable problem occurred. The app logged it and can continue."
                               : "The app encountered an unexpected problem and must close to protect the current quote state.";

            MessageBox.Show(
                FriendlyErrorMessage.FromException(args.Exception, fallback),
                "Flare Fireplace Quotes", MessageBoxButton.OK, MessageBoxImage.Warning);

            args.Handled = true;
            if (!canContinue)
                Dispatcher.BeginInvoke(() => Shutdown(1), DispatcherPriority.Send);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                var logger = Services.GetService<IAppLogger>();
                logger?.Error(ex, "Unhandled application exception.");
            }
        };
    }

    private static bool IsRecoverableUiException(Exception exception)
    {
        var root = exception.GetBaseException();
        return root is IOException or UnauthorizedAccessException or FormatException;
    }
}
