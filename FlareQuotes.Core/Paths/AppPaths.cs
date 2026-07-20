using System.Text.Json;

namespace FlareQuotes.Core.Paths;

public static class AppPaths
{
    public const string ProductFolderName = "Flare Fireplace Quotes";

    public static string Root => Ensure(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductFolderName));

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string Logs => Ensure(Path.Combine(Root, "Logs"));
    public static string LogFile => Path.Combine(Logs, "app.log");
    public static string Cache => Ensure(Path.Combine(Root, "Cache"));
    public static string Temp => Ensure(Path.Combine(Root, "Temp"));
    public static string Credentials => Ensure(Path.Combine(Root, "Credentials"));
    public static string GmailCredentialsFile => Path.Combine(Credentials, "gmail_credentials.json");
    public static string GmailTokenStore => Ensure(Path.Combine(Root, "GmailToken"));
    public static string Reports => Ensure(Path.Combine(Root, "Reports"));
    public static string WebView2 => Ensure(Path.Combine(Root, "WebView2"));
    public static string Updates => Ensure(Path.Combine(Root, "Updates"));
    public static string RecentQuotesFile => Path.Combine(Root, "recent_quotes.json");
    public static string RecentQuotesProtectedFile => Path.Combine(Root, "recent_quotes.json.dpapi");
    public static string UiSettingsFile => Path.Combine(Root, "ui-settings.json");

    public static IReadOnlyList<string>
        LegacyRoots => [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                     "Flare Fireplaces - Quotes"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                     "Flare Fireplaces - Quotes", "v3"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                     "Flare Quote Builder")];

    public static void MigrateLegacyData()
    {
        Directory.CreateDirectory(Root);
        CopyFirstExisting("settings.json", SettingsFile);
        CopyFirstExisting("ui-settings.json", UiSettingsFile);
        CopyFirstExisting("recent_quotes.json", RecentQuotesFile);
    }

    public static void ImportGmailCredentials(string? configuredPath = null)
    {
        if (File.Exists(GmailCredentialsFile))
            return;

        var candidates =
            new[] { configuredPath, Path.Combine(AppContext.BaseDirectory, "LocalData", "gmail_credentials.json") };

        var source = candidates.FirstOrDefault(IsValidCredentialSource);
        if (source is null)
            return;

        Directory.CreateDirectory(Credentials);
        var temporaryPath = GmailCredentialsFile + ".tmp";
        try
        {
            File.Copy(source, temporaryPath, overwrite: true);
            File.Move(temporaryPath, GmailCredentialsFile, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static bool IsValidCredentialSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !string.Equals(Path.GetFileName(path), "gmail_credentials.json", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path) || new FileInfo(path).Length is <= 0 or > 1024 * 1024)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path),
                                                    new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            var client = root.TryGetProperty("installed", out var installed)
                             ? installed
                             : root.TryGetProperty("web", out var web) ? web : default;

            return client.ValueKind == JsonValueKind.Object &&
                   client.TryGetProperty("client_id", out var clientId) &&
                   clientId.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(clientId.GetString()) &&
                   client.TryGetProperty("client_secret", out var clientSecret) &&
                   clientSecret.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(clientSecret.GetString());
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void CopyFirstExisting(string relativePath, string destination)
    {
        if (File.Exists(destination))
            return;

        foreach (var legacyRoot in LegacyRoots)
        {
            var source = Path.Combine(legacyRoot, relativePath);
            if (!File.Exists(source) || new FileInfo(source).Length > 16L * 1024 * 1024)
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
            return;
        }
    }

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
