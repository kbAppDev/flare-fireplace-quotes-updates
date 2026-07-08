using FlareQuotes.Core.Models;

namespace FlareQuotes.Core.Services;

public interface ISystemHealthService
{
    Task<IReadOnlyList<SystemHealthItem>> CheckAsync(CancellationToken cancellationToken = default);
}