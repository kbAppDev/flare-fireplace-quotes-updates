using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FlareQuotes.App.Services;

public static class GalleryPhotoAttachmentService
{
    private const string GalleryUrl = "https://flarefireplaces.com/fireplace-project-gallery/";
    private const string MediaSearchUrl = "https://flarefireplaces.com/wp-json/wp/v2/media?per_page=50&search=";
    private const int MaxImagesPerQuote = 4;
    private const int MaxImageBytes = 18 * 1024 * 1024;

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public static async Task<List<string>> FindExactGalleryImageAttachmentsAsync(IEnumerable<string> rawModelCodes, CancellationToken cancellationToken = default)
    {
        var modelCodes = rawModelCodes
            .SelectMany(ExtractModelCodeCandidates)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxImagesPerQuote)
            .ToList();

        if (modelCodes.Count == 0)
            return [];

        var attachments = new List<string>();

        foreach (var code in modelCodes)
        {
            var imageUrl = await FindImageUrlFromWordPressMediaAsync(code, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(imageUrl))
                imageUrl = await FindImageUrlFromGalleryPageAsync(code, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(imageUrl))
                continue;

            var localPath = await DownloadImageAsync(imageUrl, code, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(localPath))
                attachments.Add(localPath);
        }

        return attachments;
    }

    private static IEnumerable<string> ExtractModelCodeCandidates(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        var normalized = NormalizeModelCode(value);

        foreach (Match match in Regex.Matches(normalized, @"(?:VFST|VFDC|VFRC|VFLC|VST|VDC|VRC|VLC|VFF|FF|ST|LC|RC|RD|DC|TR)\d{2,3}(?:EH|H|R)", RegexOptions.IgnoreCase))
        {
            yield return match.Value.ToUpperInvariant();
        }

        if (Regex.IsMatch(normalized, @"^(?:VFST|VFDC|VFRC|VFLC|VST|VDC|VRC|VLC|VFF|FF|ST|LC|RC|RD|DC|TR)\d{2,3}(?:EH|H|R)$", RegexOptions.IgnoreCase))
            yield return normalized.ToUpperInvariant();
    }

    private static string NormalizeModelCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return Regex.Replace(value, "[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
    }

    private static async Task<string?> FindImageUrlFromWordPressMediaAsync(string modelCode, CancellationToken cancellationToken)
    {
        try
        {
            var url = MediaSearchUrl + Uri.EscapeDataString(modelCode);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("FlareQuotes/1.3.8");

            using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var item in document.RootElement.EnumerateArray())
            {
                var searchableText = BuildSearchableMediaText(item);

                if (!ContainsModelCode(searchableText, modelCode))
                    continue;

                var imageUrl = GetBestMediaImageUrl(item);

                if (!string.IsNullOrWhiteSpace(imageUrl))
                    return imageUrl;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> FindImageUrlFromGalleryPageAsync(string modelCode, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GalleryUrl);
            request.Headers.UserAgent.ParseAdd("FlareQuotes/1.3.8");

            using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            html = WebUtility.HtmlDecode(html);

            var codePattern = $@"(?<![A-Za-z0-9]){Regex.Escape(modelCode)}(?![A-Za-z0-9])";
            var match = Regex.Match(html, codePattern, RegexOptions.IgnoreCase);

            if (!match.Success)
                return null;

            var start = Math.Max(0, match.Index - 8000);
            var length = Math.Min(html.Length - start, 16000);
            var nearby = html.Substring(start, length);

            return ExtractImageCandidates(nearby)
                .Where(x => !LooksLikeLogoOrIcon(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(x => x.Contains("/wp-content/uploads/", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static string BuildSearchableMediaText(JsonElement item)
    {
        var parts = new List<string>
        {
            ReadString(item, "slug"),
            ReadString(item, "alt_text"),
            ReadRenderedString(item, "title"),
            ReadRenderedString(item, "caption"),
            ReadRenderedString(item, "description")
        };

        return StripHtml(WebUtility.HtmlDecode(string.Join(" ", parts)));
    }

    private static bool ContainsModelCode(string text, string modelCode)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(modelCode))
            return false;

        var normalizedText = NormalizeModelCode(text);

        return normalizedText.Contains(modelCode, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetBestMediaImageUrl(JsonElement item)
    {
        if (item.TryGetProperty("media_details", out var details) &&
            details.TryGetProperty("sizes", out var sizes))
        {
            foreach (var sizeName in new[] { "full", "large", "medium_large", "medium" })
            {
                if (sizes.TryGetProperty(sizeName, out var size) &&
                    size.TryGetProperty("source_url", out var sizeUrl) &&
                    sizeUrl.ValueKind == JsonValueKind.String)
                {
                    var value = NormalizeImageUrl(sizeUrl.GetString() ?? string.Empty);

                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }
        }

        var source = ReadString(item, "source_url");
        return NormalizeImageUrl(source);
    }

    private static string ReadString(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string ReadRenderedString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
            return string.Empty;

        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("rendered", out var rendered) &&
            rendered.ValueKind == JsonValueKind.String)
        {
            return rendered.GetString() ?? string.Empty;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string StripHtml(string value)
    {
        return Regex.Replace(value ?? string.Empty, "<.*?>", " ");
    }

    private static IEnumerable<string> ExtractImageCandidates(string htmlFragment)
    {
        var attributePattern = @"(?:href|src|data-src|data-full|data-orig-file|data-large_image)\s*=\s*[""'](?<url>[^""']+\.(?:jpg|jpeg|png|webp)(?:\?[^""']*)?)[""']";

        foreach (Match match in Regex.Matches(htmlFragment, attributePattern, RegexOptions.IgnoreCase))
        {
            var url = NormalizeImageUrl(match.Groups["url"].Value);

            if (!string.IsNullOrWhiteSpace(url))
                yield return url;
        }

        var backgroundPattern = @"url\((?<url>[^)]+?\.(?:jpg|jpeg|png|webp)(?:\?[^)]*)?)\)";

        foreach (Match match in Regex.Matches(htmlFragment, backgroundPattern, RegexOptions.IgnoreCase))
        {
            var url = NormalizeImageUrl(match.Groups["url"].Value.Trim('\'', '"', ' '));

            if (!string.IsNullOrWhiteSpace(url))
                yield return url;
        }
    }

    private static string NormalizeImageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var url = WebUtility.HtmlDecode(value.Trim());

        if (url.StartsWith("//", StringComparison.Ordinal))
            url = "https:" + url;
        else if (url.StartsWith("/", StringComparison.Ordinal))
            url = "https://flarefireplaces.com" + url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            return string.Empty;

        return url;
    }

    private static bool LooksLikeLogoOrIcon(string url)
    {
        var lower = url.ToLowerInvariant();

        return lower.Contains("logo")
            || lower.Contains("favicon")
            || lower.Contains("icon")
            || lower.Contains("sprite")
            || lower.Contains("placeholder");
    }

    private static async Task<string?> DownloadImageAsync(string imageUrl, string modelCode, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            request.Headers.UserAgent.ParseAdd("FlareQuotes/1.3.8");

            using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            if (bytes.Length == 0 || bytes.Length > MaxImageBytes)
                return null;

            var extension = GetExtensionFromUrl(imageUrl);
            var folder = Path.Combine(Path.GetTempPath(), "FlareQuotes", "GalleryAttachments");

            Directory.CreateDirectory(folder);

            var outputPath = Path.Combine(folder, $"Flare Fireplace Gallery - {modelCode}{extension}");
            await File.WriteAllBytesAsync(outputPath, bytes, cancellationToken).ConfigureAwait(false);

            return outputPath;
        }
        catch
        {
            return null;
        }
    }

    private static string GetExtensionFromUrl(string imageUrl)
    {
        var path = Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath
            : imageUrl;

        var extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" => extension,
            _ => ".jpg"
        };
    }
}


