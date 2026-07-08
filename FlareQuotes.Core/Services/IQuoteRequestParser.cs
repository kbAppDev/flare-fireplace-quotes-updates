using FlareQuotes.Core.Models;

namespace FlareQuotes.Core.Services;

public interface IQuoteRequestParser
{
    QuoteRequest Parse(string rawText);
}
