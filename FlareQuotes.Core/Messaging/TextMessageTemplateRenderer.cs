using System.Text.RegularExpressions;
using FlareQuotes.Core.Models;

namespace FlareQuotes.Core.Messaging;

public static class TextMessageTemplateRenderer
{
    public const string DefaultConsultationUrl = "https://meetings.hubspot.com/kyle533/jobsite-consultation";
    public static IReadOnlyList<string> SupportedTokens { get; } =
        ["FirstName", "Project", "Model", "Consultation", "SalesName"];

    private static readonly Regex TokenPattern = new(@"\{([^{}\r\n]+)\}", RegexOptions.CultureInvariant,
                                                       TimeSpan.FromMilliseconds(100));

    public static TextMessageRenderResult Render(string? body, TextMessageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var firstName = context.ClientName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                               .FirstOrDefault() ?? string.Empty;
        var values = new Dictionary<string, (string Value, string Fallback)>(StringComparer.OrdinalIgnoreCase)
        {
            ["FirstName"] = (firstName, "there"),
            ["Project"] = (context.ProjectName.Trim(), "your project"),
            ["Model"] = (context.Model.Trim(), "fireplace"),
            ["Consultation"] = (context.ConsultationUrl.Trim(), DefaultConsultationUrl),
            ["SalesName"] = (context.SalesName.Trim(), string.Empty)
        };
        var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rendered = TokenPattern.Replace(body ?? string.Empty, match =>
        {
            var token = match.Groups[1].Value.Trim();
            if (!values.TryGetValue(token, out var replacement))
            {
                unknown.Add(token);
                return match.Value;
            }

            if (!string.IsNullOrWhiteSpace(replacement.Value))
                return replacement.Value;

            missing.Add(SupportedTokens.First(name => name.Equals(token, StringComparison.OrdinalIgnoreCase)));
            return replacement.Fallback;
        });

        return new TextMessageRenderResult(rendered, unknown.ToArray(), missing.ToArray());
    }
}
