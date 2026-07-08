using FlareQuotes.Core.Models;

namespace FlareQuotes.Core.Services;

public interface ISecurityAuditService
{
    Task<IReadOnlyList<SystemHealthItem>> AuditAsync(CancellationToken cancellationToken = default);
}