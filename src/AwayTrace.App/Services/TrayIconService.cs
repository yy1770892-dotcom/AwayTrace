using System.Drawing;
using Forms = System.Windows.Forms;

namespace AwayTrace.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _stopProtectionItem;
    private bool _disposed;

    public TrayIconService()
    {
        _stopProtectionItem = new Forms.ToolStripMenuItem("보호 종료");
        _stopProtectionItem.Click += (_, _) => StopProtectionRequested?.Invoke(this, EventArgs.Empty);

        var showItem = new Forms.ToolStripMenuItem("열기");
        showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new Forms.ToolStripMenuItem("종료");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(showItem);
        menu.Items.Add(_stopProtectionItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "AwayTrace - 보호 해제",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        SetProtectionActive(false);
    }

    public event EventHandler? ShowRequested;

    public event EventHandler? StopProtectionRequested;

    public event EventHandler? ExitRequested;

    public void SetProtectionActive(bool isActive)
    {
        _notifyIcon.Text = isActive ? "AwayTrace - 보호 중" : "AwayTrace - 보호 해제";
        _stopProtectionItem.Enabled = isActive;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _disposed = true;
    }
}
