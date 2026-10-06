using FlareQuotes.Core.Messaging;
using FlareQuotes.Core.Models;
using Xunit;

namespace FlareQuotes.Tests.MessagingTests;

public sealed class TextMessageTemplateRendererTests
{
    [Fact]
    public void ExpandsEverySupportedTokenAsPlainTextWithoutChangingTemplate()
    {
        const string body = "Hi {FirstName}, your {Model} quote for {Project} is ready.\n{Consultation}\n— {SalesName}";
        var context = new TextMessageContext
        {
            ClientName = "Amanda Jensen",
            ProjectName = "Jensen's Lakeview Renovation",
            Model = "DVFF60H",
            ConsultationUrl = TextMessageTemplateRenderer.DefaultConsultationUrl,
            SalesName = "Kyle"
        };

        var rendered = TextMessageTemplateRenderer.Render(body, context);

        Assert.Equal("Hi Amanda, your DVFF60H quote for Jensen's Lakeview Renovation is ready.\n" +
                         TextMessageTemplateRenderer.DefaultConsultationUrl + "\n— Kyle", rendered.Text);
        Assert.Empty(rendered.UnknownTokens);
        Assert.Empty(rendered.MissingValues);
        Assert.Contains("{FirstName}", body);
    }

    [Fact]
    public void UnknownTokensRemainVisibleAndProduceNonblockingFeedback()
    {
        var rendered = TextMessageTemplateRenderer.Render("Hi {FirstName}, {Other} and {other}.",
            new TextMessageContext { ClientName = "Amanda" });

        Assert.Equal("Hi Amanda, {Other} and {other}.", rendered.Text);
        Assert.Single(rendered.UnknownTokens);
        Assert.Equal("Other", rendered.UnknownTokens[0]);
    }

    [Fact]
    public void MissingQuoteDetailsUseReadableFallbacksAndAreReported()
    {
        var rendered = TextMessageTemplateRenderer.Render("Hi {FirstName}: {Project} / {Model} / {SalesName}",
            new TextMessageContext());

        Assert.Equal("Hi there: your project / fireplace / ", rendered.Text);
        Assert.Equal(["FirstName", "Project", "Model", "SalesName"], rendered.MissingValues);
    }

    [Fact]
    public void TokensAcceptMixedCaseAndValuesAreNotRecursivelyExpanded()
    {
        var rendered = TextMessageTemplateRenderer.Render("{ firstName } / {PROJECT}", new TextMessageContext
        {
            ClientName = "Amanda Jensen",
            ProjectName = "Villa {Model}",
            Model = "DVFF60"
        });

        Assert.Equal("Amanda / Villa {Model}", rendered.Text);
        Assert.Empty(rendered.UnknownTokens);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyTemplateProducesEmptyPreview(string? body)
    {
        var rendered = TextMessageTemplateRenderer.Render(body, new TextMessageContext());

        Assert.Empty(rendered.Text);
        Assert.Empty(rendered.UnknownTokens);
    }
}
