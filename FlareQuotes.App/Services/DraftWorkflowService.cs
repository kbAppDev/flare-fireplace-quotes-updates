using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlareQuotes.Core.Email;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Services;

namespace FlareQuotes.App.Services;

public sealed record DraftWorkflowInput(QuoteRequest Request, PricedQuoteResult PricedQuote,
                                        IReadOnlyList<ResourceLinkSet> ResourceSets, string PdfPath,
                                        IReadOnlyList<string> ManualAttachments, AppSettings Settings,
                                        string ModelSummary);

public sealed class DraftWorkflowService
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(90);

    private readonly IGmailDraftService _gmail;
    private readonly EmailTemplateService _templates;
    private readonly IAppLogger _logger;

    public DraftWorkflowService(IGmailDraftService gmail, EmailTemplateService templates, IAppLogger logger)
    {
        _gmail = gmail;
        _templates = templates;
        _logger = logger;
    }

    public async Task<EmailDraftResult> ExecuteAsync(DraftWorkflowInput input,
                                                     CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OperationTimeout);

        try
        {
            if (string.IsNullOrWhiteSpace(input.PdfPath) || !File.Exists(input.PdfPath))
            {
                return new EmailDraftResult { Success = false,
                                              Message = "The generated quote PDF could not be found." };
            }

            var validAttachments =
                input.ManualAttachments.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            var signature = input.Settings.UseGmailSignature
                                ? await _gmail.GetSignatureHtmlAsync(timeout.Token).ConfigureAwait(false)
                                : string.Empty;

            var html =
                _templates.BuildHtml(input.Request, input.PricedQuote, input.ResourceSets, input.Settings, signature);
            var subject = _templates.BuildSubject(input.Request, input.PricedQuote);
            var pdfInfo = new FileInfo(input.PdfPath);
            var attachmentBytes = validAttachments.Sum(path => new FileInfo(path).Length);

            _logger.Info($"Email copy prepared. SubjectLength={subject.Length}; HtmlLength={html.Length}; " +
                         $"ResourceSets={input.ResourceSets.Count}; Models={input.ModelSummary}.");
            _logger.Info(
                $"Attachments prepared. ManualCount={validAttachments.Count}; ManualBytes={attachmentBytes}; " +
                $"PdfBytes={pdfInfo.Length}; Models={input.ModelSummary}.");

            return await _gmail
                .CreateDraftAsync(new EmailDraftRequest { ToEmail = input.Request.Email,
                                                          BccEmail = input.Settings.HubSpotBcc, Subject = subject,
                                                          HtmlBody = html, PdfAttachmentPath = input.PdfPath,
                                                          AdditionalAttachmentPaths = validAttachments },
                                  timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warning($"Draft workflow timed out. Models={input.ModelSummary}.");
            return new EmailDraftResult { Success = false,
                                          Message =
                                              "Gmail draft creation timed out. Check the connection and try again." };
        }
    }
}
