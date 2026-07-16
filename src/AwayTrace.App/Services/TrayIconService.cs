using System.Drawing;
using System.IO;
using Forms = System.Windows.Forms;

namespace AwayTrace.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Icon _icon;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _stopProtectionItem;
    private bool _disposed;

    public TrayIconService()
    {
        _stopProtectionItem = new Forms.ToolStripMenuItem("보호 종료");
        _stopProtectionItem.Click += (_, _) => StopProtectionRequested?.Invoke(this, EventArgs.Empty);

        var showItem = new Forms.ToolStripMenuItem("창 열기");
        showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new Forms.ToolStripMenuItem("종료");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(showItem);
        menu.Items.Add(_stopProtectionItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _icon = LoadAppIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        SetProtectionActive(false);
        ShowInfo("AwayTrace 실행 중", "창이 숨겨지면 Ctrl+Alt+A 또는 AwayTrace 재실행으로 다시 열 수 있습니다.");
    }

    public event EventHandler? ShowRequested;

    public event EventHandler? StopProtectionRequested;

    public event EventHandler? ExitRequested;

    public void SetProtectionActive(bool isActive)
    {
        _notifyIcon.Visible = true;
        _notifyIcon.Text = isActive ? "AwayTrace - 보호 중" : "AwayTrace - 보호 해제";
        _stopProtectionItem.Enabled = isActive;
    }

    public void SetVisible(bool visible)
    {
        _notifyIcon.Visible = visible;
    }

    public void ShowInfo(string title, string message)
    {
        _notifyIcon.Visible = true;
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
        _disposed = true;
    }

    private static Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AwayTrace.ico");
        return File.Exists(iconPath)
            ? new Icon(iconPath)
            : (Icon)SystemIcons.Shield.Clone();
    }
}
