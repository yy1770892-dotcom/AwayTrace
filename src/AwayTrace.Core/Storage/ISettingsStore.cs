namespace AwayTrace.Core.Storage;

public interface ISettingsStore
{
    Task<string?> GetSettingAsync(string key);

    Task SetSettingAsync(string key, string value);
}
