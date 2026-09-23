using System.Security.Cryptography;

namespace FlareQuotes.Core.Models;

public static class QuoteNumberGenerator
{
    public static string Create() =>
        $"Q-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{RandomNumberGenerator.GetHexString(6)}";
}
