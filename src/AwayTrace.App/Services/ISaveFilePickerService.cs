namespace AwayTrace.App.Services;

public interface ISaveFilePickerService
{
    string? PickSaveFile(string title, string filter, string defaultFileName);
}
