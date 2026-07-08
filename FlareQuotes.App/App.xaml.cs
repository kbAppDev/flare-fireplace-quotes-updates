using System.Windows;
using System.Windows.Threading;
using FlareQuotes.App.ViewModels;
using FlareQuotes.Core.Email;
using FlareQuotes.Core.Features;
using FlareQuotes.Core.Media;
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
        collection.AddSingleton<ISettingsService, JsonSettingsService>();
        collection.AddSingleton<IUpdateService, HttpUpdateService>();
        collection.AddSingleton<EmailTemplateService>();
        collection.AddTransient<MainViewModel>();

        Services = collection.BuildServiceProvider();

        InstallGlobalExceptionHandling();

        base.OnStartup(e);
    }

    private void InstallGlobalExceptionHandling()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            var logger = Services.GetService<IAppLogger>();
            logger?.Error(args.Exception, "Unhandled UI exception.");

            MessageBox.Show(
                FriendlyErrorMessage.FromException(args.Exception, "Something unexpected happened. The app logged the issue and will keep running if possible."),
                "Flare Fireplace Quotes",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            args.Handled = true;
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
}