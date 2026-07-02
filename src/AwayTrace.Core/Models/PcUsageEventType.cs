namespace AwayTrace.Core.Models;

public enum PcUsageEventType
{
    AppStarted,
    AppExited,
    SessionLocked,
    SessionUnlocked,
    SystemStarted,
    SystemShutdown,
    UnexpectedShutdown,
    Unknown
}
