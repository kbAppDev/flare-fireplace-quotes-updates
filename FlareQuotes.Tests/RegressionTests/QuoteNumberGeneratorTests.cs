using FlareQuotes.Core.Models;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class QuoteNumberGeneratorTests
{
    [Fact]
    public void NewQuoteRequestsReceiveDistinctSortableNumbers()
    {
        var first = new QuoteRequest().QuoteNumber;
        var second = new QuoteRequest().QuoteNumber;

        Assert.Matches(@"^Q-\d{8}-\d{6}-[A-F0-9]{6}$", first);
        Assert.Matches(@"^Q-\d{8}-\d{6}-[A-F0-9]{6}$", second);
        Assert.NotEqual(first, second);
    }
}
