using AwayTrace.Core.Models;
using AwayTrace.Core.Services;
using AwayTrace.Core.Storage;

var tests = new (string Name, Func<Task> Body)[]
{
    ("FileChangeDebouncer suppresses duplicates inside the debounce window", FileChangeDebouncerSuppressesDuplicates),
    ("FileChangeDebouncer allows the same event after the debounce window", FileChangeDebouncerAllowsAfterWindow),
    ("FileChangeDebouncer includes old path for rename keys", FileChangeDebouncerUsesOldPathForRename),
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
