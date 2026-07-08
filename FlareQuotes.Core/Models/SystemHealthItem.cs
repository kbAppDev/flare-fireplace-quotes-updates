namespace FlareQuotes.Core.Models;

public sealed class SystemHealthItem
{
    public string Name { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public SystemHealthState State { get; set; } = SystemHealthState.Ok;

    public string StateText => State switch
    {
        SystemHealthState.Ok => "Ready",
        SystemHealthState.Warning => "Review",
        SystemHealthState.Error => "Needs Attention",
        _ => "Unknown"
    };

    public string Icon => State switch
    {
        SystemHealthState.Ok => "●",
        SystemHealthState.Warning => "●",
        SystemHealthState.Error => "●",
        _ => "●"
    };
}

public enum SystemHealthState
{
    Ok,
    Warning,
    Error
}