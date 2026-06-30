using AwayTrace.Core.Storage;

namespace AwayTrace.Core.Services;

public sealed class SessionRecoveryService
{
    private readonly ISessionRepository _sessions;

    public SessionRecoveryService(ISessionRepository sessions)
    {
        _sessions = sessions;
    }

    public async Task<int> RecoverAbandonedSessionsAsync(DateTimeOffset now)
    {
        var activeSessions = await _sessions.GetActiveSessionsAsync();
        foreach (var session in activeSessions)
        {
            await _sessions.MarkSessionAbnormalAsync(
                session.Id,
                now,
                "앱이 정상적인 보호 종료 절차를 기록하지 못했습니다.");
        }

        return activeSessions.Count;
    }
}
