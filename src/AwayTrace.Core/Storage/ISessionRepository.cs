using AwayTrace.Core.Models;

namespace AwayTrace.Core.Storage;

public interface ISessionRepository
{
    Task<IReadOnlyList<WatchSession>> GetActiveSessionsAsync();

    Task MarkSessionAbnormalAsync(Guid sessionId, DateTimeOffset endedAt, string reason);
}
