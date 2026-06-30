using System.Windows.Forms;

namespace AwayTrace.App.Services;

public sealed class FolderPickerService : IFolderPickerService
{
    public string? PickFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "감시할 업무 폴더를 선택하세요.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }
}
