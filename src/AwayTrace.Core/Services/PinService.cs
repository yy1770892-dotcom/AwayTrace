using System.Security.Cryptography;
using AwayTrace.Core.Models;
using AwayTrace.Core.Storage;

namespace AwayTrace.Core.Services;

public sealed class PinService
{
    private const string SaltKey = "pin.salt";
    private const string HashKey = "pin.hash";
    private const string IterationsKey = "pin.iterations";
    private const string FailureCountKey = "pin.failure_count";
    private const string LockoutUntilKey = "pin.lockout_until";
    private const int HashBytes = 32;
    private const int SaltBytes = 32;
    private const int Iterations = 100_000;
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(30);

    private readonly ISettingsStore _settings;
    private readonly TimeProvider _timeProvider;

    public PinService(ISettingsStore settings, TimeProvider? timeProvider = null)
    {
        _settings = settings;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<bool> HasPinAsync()
    {
        return !string.IsNullOrWhiteSpace(await _settings.GetSettingAsync(HashKey));
    }

    public async Task SetPinAsync(string pin)
    {
        ValidatePin(pin);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            pin,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashBytes);

        await _settings.SetSettingAsync(SaltKey, Convert.ToBase64String(salt));
        await _settings.SetSettingAsync(HashKey, Convert.ToBase64String(hash));
        await _settings.SetSettingAsync(IterationsKey, Iterations.ToString());
        await _settings.SetSettingAsync(FailureCountKey, "0");
        await _settings.SetSettingAsync(LockoutUntilKey, string.Empty);
    }

    public async Task<PinVerifyResult> VerifyAsync(string pin)
    {
        var storedHash = await _settings.GetSettingAsync(HashKey);
        var storedSalt = await _settings.GetSettingAsync(SaltKey);
        if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrWhiteSpace(storedSalt))
        {
            return PinVerifyResult.NotConfigured();
        }

        var now = _timeProvider.GetUtcNow();
        var lockoutUntil = await GetLockoutUntilAsync();
        if (lockoutUntil is not null && lockoutUntil > now)
        {
            return PinVerifyResult.Locked(lockoutUntil.Value - now);
        }

        var iterationsText = await _settings.GetSettingAsync(IterationsKey);
        var iterations = int.TryParse(iterationsText, out var parsedIterations) ? parsedIterations : Iterations;
        var salt = Convert.FromBase64String(storedSalt);
        var expected = Convert.FromBase64String(storedHash);
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            pin,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        if (CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            await _settings.SetSettingAsync(FailureCountKey, "0");
            await _settings.SetSettingAsync(LockoutUntilKey, string.Empty);
            return PinVerifyResult.Success();
        }

        var failures = await GetFailureCountAsync() + 1;
        if (failures >= MaxFailures)
        {
            await _settings.SetSettingAsync(FailureCountKey, failures.ToString());
            await _settings.SetSettingAsync(LockoutUntilKey, (now + LockoutDuration).ToString("O"));
            return PinVerifyResult.Locked(LockoutDuration);
        }

        await _settings.SetSettingAsync(FailureCountKey, failures.ToString());
        return PinVerifyResult.Failed(MaxFailures - failures);
    }

    public static void ValidatePin(string pin)
    {
        if (pin.Length < 6 || pin.Any(c => !char.IsDigit(c)))
        {
            throw new ArgumentException("PIN은 숫자 6자리 이상이어야 합니다.", nameof(pin));
        }
    }

    private async Task<int> GetFailureCountAsync()
    {
        var value = await _settings.GetSettingAsync(FailureCountKey);
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }

    private async Task<DateTimeOffset?> GetLockoutUntilAsync()
    {
        var value = await _settings.GetSettingAsync(LockoutUntilKey);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }
}
