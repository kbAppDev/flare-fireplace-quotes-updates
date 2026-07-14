using FlareQuotes.Core.Models;

namespace FlareQuotes.Core.Settings;

public static class AppSettingsRuntimeCache
{
    private static readonly object Sync = new();
    private static AppSettings? _current;

    public static AppSettings? Current
    {
        get {
            lock (Sync)
            {
                return _current;
            }
        }
    }

    public static void Set(AppSettings settings)
    {
        lock (Sync)
        {
            _current = settings;
        }
    }
}
