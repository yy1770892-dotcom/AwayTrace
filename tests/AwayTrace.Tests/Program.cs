using AwayTrace.Core.Models;
using AwayTrace.Core.Services;
using AwayTrace.Core.Storage;

var tests = new (string Name, Func<Task> Body)[]
{
    ("FileChangeDebouncer suppresses duplicates inside the debounce window", FileChangeDebouncerSuppressesDuplicates),
    ("FileChangeDebouncer allows the same event after the debounce window", FileChangeDebouncerAllowsAfterWindow),
    ("FileChangeDebouncer includes old path for rename keys", FileChangeDebouncerUsesOldPathForRename),
    ("ProtectedAppScanSpeed keeps normal and fast polling intervals", ProtectedAppScanSpeedKeepsPollingIntervals),
    ("PcUsageSchedule detects outside standard hours", PcUsageScheduleDetectsOutsideStandardHours),
    ("PcUsageSchedule supports overnight standard hours", PcUsageScheduleSupportsOvernightHours),
    ("AwayTraceDatabase stores PC usage events", PcUsageEventsPersistInDatabase),
    ("AwayTraceDatabase keeps protected apps after reopening", ProtectedAppsPersistAfterReopeningDatabase),
    ("AwayTraceDatabase stores monitored folders by kind", MonitoredFoldersPersistWithKinds),
    ("AwayTraceDatabase preserves low confidence after ending a session", EndSessionPreservesLowConfidence),
    ("AwayTraceDatabase orders timestamps correctly across UTC offsets", TimestampOrderingIsCorrectAcrossOffsets),
    ("SessionRecoveryService marks active sessions as abnormal", SessionRecoveryMarksActiveSessionsAbnormal),
    ("SessionRecoveryService leaves empty repositories unchanged", SessionRecoveryDoesNothingWithoutActiveSessions)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.WriteLine($"FAIL {test.Name}");
        Console.WriteLine(ex);
    }
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"{failures.Count} test(s) failed.");
    Environment.Exit(1);
}

Console.WriteLine();
Console.WriteLine($"{tests.Length} test(s) passed.");

static Task FileChangeDebouncerSuppressesDuplicates()
{
    var debouncer = new FileChangeDebouncer(TimeSpan.FromSeconds(2));
    var now = DateTimeOffset.Parse("2026-06-30T10:00:00+09:00");

    Assert.True(debouncer.ShouldRecord(FileEventType.Changed, @"C:\Work\a.txt", null, now));
    Assert.False(debouncer.ShouldRecord(FileEventType.Changed, @"C:\Work\a.txt", null, now.AddSeconds(1)));
    return Task.CompletedTask;
}

static Task FileChangeDebouncerAllowsAfterWindow()
{
    var debouncer = new FileChangeDebouncer(TimeSpan.FromSeconds(2));
    var now = DateTimeOffset.Parse("2026-06-30T10:00:00+09:00");

    Assert.True(debouncer.ShouldRecord(FileEventType.Changed, @"C:\Work\a.txt", null, now));
    Assert.True(debouncer.ShouldRecord(FileEventType.Changed, @"C:\Work\a.txt", null, now.AddSeconds(2)));
    return Task.CompletedTask;
}

static Task FileChangeDebouncerUsesOldPathForRename()
{
    var debouncer = new FileChangeDebouncer(TimeSpan.FromSeconds(2));
    var now = DateTimeOffset.Parse("2026-06-30T10:00:00+09:00");

    Assert.True(debouncer.ShouldRecord(FileEventType.Renamed, @"C:\Work\b.txt", @"C:\Work\a.txt", now));
    Assert.False(debouncer.ShouldRecord(FileEventType.Renamed, @"C:\Work\b.txt", @"C:\Work\a.txt", now.AddMilliseconds(500)));
    Assert.True(debouncer.ShouldRecord(FileEventType.Renamed, @"C:\Work\b.txt", @"C:\Work\other.txt", now.AddMilliseconds(600)));
    return Task.CompletedTask;
}

static Task ProtectedAppScanSpeedKeepsPollingIntervals()
{
    Assert.Equal(250, (int)ProtectedAppScanSpeed.Normal);
    Assert.Equal(100, (int)ProtectedAppScanSpeed.Fast);
    return Task.CompletedTask;
}

static Task PcUsageScheduleDetectsOutsideStandardHours()
{
    var schedule = new PcUsageSchedule(new TimeOnly(9, 0), new TimeOnly(18, 0));

    Assert.False(schedule.IsOutside(DateTimeOffset.Parse("2026-07-02T10:00:00+09:00")));
    Assert.True(schedule.IsOutside(DateTimeOffset.Parse("2026-07-02T22:00:00+09:00")));
    return Task.CompletedTask;
}

static Task PcUsageScheduleSupportsOvernightHours()
{
    var schedule = new PcUsageSchedule(new TimeOnly(22, 0), new TimeOnly(6, 0));

    Assert.False(schedule.IsOutside(DateTimeOffset.Parse("2026-07-02T23:00:00+09:00")));
    Assert.False(schedule.IsOutside(DateTimeOffset.Parse("2026-07-02T05:30:00+09:00")));
    Assert.True(schedule.IsOutside(DateTimeOffset.Parse("2026-07-02T12:00:00+09:00")));
    return Task.CompletedTask;
}

static async Task PcUsageEventsPersistInDatabase()
{
    var dbPath = Path.Combine(Path.GetTempPath(), $"awaytrace-test-{Guid.NewGuid():N}.db");
    try
    {
        var first = new AwayTraceDatabase(dbPath);
        first.Initialize();
        await first.AddPcUsageEventAsync(new PcUsageEvent(
            0,
            DateTimeOffset.Parse("2026-07-02T08:10:00+09:00"),
            PcUsageEventType.AppStarted,
            "AwayTrace 실행",
            "AwayTrace"));

        var second = new AwayTraceDatabase(dbPath);
        second.Initialize();
        var events = await second.GetPcUsageEventsAsync(DateTimeOffset.Parse("2026-07-01T00:00:00+09:00"), 20);

        Assert.Equal(1, events.Count);
        Assert.Equal(PcUsageEventType.AppStarted, events[0].EventType);
        Assert.Equal("AwayTrace 실행", events[0].Description);
        Assert.Equal("AwayTrace", events[0].Source);
    }
    finally
    {
        TryDelete(dbPath);
        TryDelete(dbPath + "-wal");
        TryDelete(dbPath + "-shm");
    }
}

static async Task ProtectedAppsPersistAfterReopeningDatabase()
{
    var dbPath = Path.Combine(Path.GetTempPath(), $"awaytrace-test-{Guid.NewGuid():N}.db");
    try
    {
        var first = new AwayTraceDatabase(dbPath);
        first.Initialize();
        await first.AddProtectedAppAsync("업무 메신저", "WorkMessenger.exe", @"C:\Tools\WorkMessenger.exe");

        var second = new AwayTraceDatabase(dbPath);
        second.Initialize();
        var apps = await second.GetProtectedAppsAsync();

        Assert.Equal(1, apps.Count);
        Assert.Equal("업무 메신저", apps[0].DisplayName);
        Assert.Equal("WorkMessenger", apps[0].ProcessName);
        Assert.Equal(@"C:\Tools\WorkMessenger.exe", apps[0].ExecutablePath);
        Assert.True(apps[0].IsEnabled);
    }
    finally
    {
        TryDelete(dbPath);
        TryDelete(dbPath + "-wal");
        TryDelete(dbPath + "-shm");
    }
}

static async Task MonitoredFoldersPersistWithKinds()
{
    var dbPath = Path.Combine(Path.GetTempPath(), $"awaytrace-test-{Guid.NewGuid():N}.db");
    var recordPath = Path.Combine(Path.GetTempPath(), $"awaytrace-record-{Guid.NewGuid():N}");
    var lockPath = Path.Combine(Path.GetTempPath(), $"awaytrace-lock-{Guid.NewGuid():N}");
    try
    {
        var first = new AwayTraceDatabase(dbPath);
        first.Initialize();
        await first.AddFolderAsync(recordPath);
        await first.AddFolderAsync(lockPath, MonitoredFolderKind.Locked);
        await first.AddFolderAsync(recordPath, MonitoredFolderKind.Locked);

        var second = new AwayTraceDatabase(dbPath);
        second.Initialize();
        var folders = await second.GetFoldersAsync();

        Assert.Equal(2, folders.Count);
        Assert.Equal(MonitoredFolderKind.Locked, folders.Single(folder => folder.Path == recordPath).Kind);
        Assert.Equal(MonitoredFolderKind.Locked, folders.Single(folder => folder.Path == lockPath).Kind);
    }
    finally
    {
        TryDelete(dbPath);
        TryDelete(dbPath + "-wal");
        TryDelete(dbPath + "-shm");
    }
}

static async Task EndSessionPreservesLowConfidence()
{
    var dbPath = Path.Combine(Path.GetTempPath(), $"awaytrace-test-{Guid.NewGuid():N}.db");
    try
    {
        var db = new AwayTraceDatabase(dbPath);
        db.Initialize();

        var sessionId = Guid.NewGuid();
        await db.CreateSessionAsync(new WatchSession(
            sessionId,
            DateTimeOffset.Parse("2026-07-02T09:00:00+09:00"),
            null,
            ProtectionSessionState.Active,
            false,
            "[]",
            null));

        // 재부팅 복구 시나리오: 기록 공백으로 신뢰도 낮음 표시 후 정상 종료.
        await db.MarkSessionLowConfidenceAsync(sessionId);
        await db.EndSessionAsync(sessionId, DateTimeOffset.Parse("2026-07-02T10:00:00+09:00"));

        var session = await db.GetSessionAsync(sessionId);
        Assert.True(session is not null, "Session must exist after ending.");
        Assert.True(session!.LowConfidence, "low_confidence must survive EndSessionAsync.");
        Assert.Equal(ProtectionSessionState.Completed, session.State);
    }
    finally
    {
        TryDelete(dbPath);
        TryDelete(dbPath + "-wal");
        TryDelete(dbPath + "-shm");
    }
}

static async Task TimestampOrderingIsCorrectAcrossOffsets()
{
    var dbPath = Path.Combine(Path.GetTempPath(), $"awaytrace-test-{Guid.NewGuid():N}.db");
    try
    {
        var db = new AwayTraceDatabase(dbPath);
        db.Initialize();

        // 실제 시각: A = 01:00Z, B = 02:00Z (B가 더 나중).
        // 오프셋을 섞어 저장해도 UTC로 정규화되어 B가 먼저(내림차순) 나와야 한다.
        await db.AddPcUsageEventAsync(new PcUsageEvent(
            0,
            DateTimeOffset.Parse("2026-07-02T10:00:00+09:00"),
            PcUsageEventType.AppStarted,
            "A",
            "AwayTrace"));
        await db.AddPcUsageEventAsync(new PcUsageEvent(
            0,
            DateTimeOffset.Parse("2026-07-02T02:00:00+00:00"),
            PcUsageEventType.AppExited,
            "B",
            "AwayTrace"));

        var events = await db.GetPcUsageEventsAsync(DateTimeOffset.Parse("2026-07-01T00:00:00+00:00"), 10);

        Assert.Equal(2, events.Count);
        Assert.Equal("B", events[0].Description);
        Assert.Equal("A", events[1].Description);
    }
    finally
    {
        TryDelete(dbPath);
        TryDelete(dbPath + "-wal");
        TryDelete(dbPath + "-shm");
    }
}

static async Task SessionRecoveryMarksActiveSessionsAbnormal()
{
    var repository = new FakeSessionRepository(
        new WatchSession(
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-06-30T09:00:00+09:00"),
            null,
            ProtectionSessionState.Active,
            false,
            "[]",
            null));

    var service = new SessionRecoveryService(repository);
    var recovered = await service.RecoverAbandonedSessionsAsync(DateTimeOffset.Parse("2026-06-30T10:00:00+09:00"));

    Assert.Equal(1, recovered);
    Assert.Equal(1, repository.MarkedAbnormal.Count);
    Assert.True(repository.MarkedAbnormal[0].Reason.Contains("정상적인 보호 종료", StringComparison.Ordinal));
}

static void TryDelete(string path)
{
    try
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
    catch (IOException)
    {
    }
}

static async Task SessionRecoveryDoesNothingWithoutActiveSessions()
{
    var repository = new FakeSessionRepository();
    var service = new SessionRecoveryService(repository);
    var recovered = await service.RecoverAbandonedSessionsAsync(DateTimeOffset.Parse("2026-06-30T10:00:00+09:00"));

    Assert.Equal(0, recovered);
    Assert.Equal(0, repository.MarkedAbnormal.Count);
}

internal static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message ?? "Expected true.");
        }
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition)
        {
            throw new InvalidOperationException(message ?? "Expected false.");
        }
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
        }
    }
}

internal sealed class FakeSessionRepository : ISessionRepository
{
    private readonly IReadOnlyList<WatchSession> _activeSessions;

    public FakeSessionRepository(params WatchSession[] activeSessions)
    {
        _activeSessions = activeSessions;
    }

    public List<(Guid SessionId, DateTimeOffset EndedAt, string Reason)> MarkedAbnormal { get; } = [];

    public Task<IReadOnlyList<WatchSession>> GetActiveSessionsAsync()
    {
        return Task.FromResult(_activeSessions);
    }

    public Task MarkSessionAbnormalAsync(Guid sessionId, DateTimeOffset endedAt, string reason)
    {
        MarkedAbnormal.Add((sessionId, endedAt, reason));
        return Task.CompletedTask;
    }
}
