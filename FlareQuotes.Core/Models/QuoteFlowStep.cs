namespace FlareQuotes.Core.Models;

public enum QuoteFlowStepState
{
    Pending,
    Running,
    Complete,
    Warning,
    Failed
}

public sealed class QuoteFlowStep
{
    public string Name { get; set; } = string.Empty;
    public QuoteFlowStepState State { get; set; } = QuoteFlowStepState.Pending;
    public string Detail { get; set; } = string.Empty;
}
