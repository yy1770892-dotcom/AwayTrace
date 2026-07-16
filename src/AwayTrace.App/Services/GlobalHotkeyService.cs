using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace AwayTrace.App.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int DefaultHotkeyId = 0x4154;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private HwndSource? _source;
    private IntPtr _handle;
    private Action? _callback;
    private bool _registered;
    private readonly int _hotkeyId;

    public GlobalHotkeyService(int hotkeyId = DefaultHotkeyId)
    {
        _hotkeyId = hotkeyId;
    }

    public void Bind(Window window, Action callback)
    {
        _handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
        _callback = callback;
    }

    public bool Configure(bool enabled, string gestureText, out string? error)
    {
        error = null;
        Unregister();

        if (!enabled)
        {
            return true;
        }

        if (_handle == IntPtr.Zero)
        {
            error = "단축키를 등록할 창 핸들이 아직 준비되지 않았습니다.";
            return false;
        }

        if (!TryParseGesture(gestureText, out var modifiers, out var virtualKey, out error))
        {
            return false;
        }

        _registered = RegisterHotKey(_handle, _hotkeyId, modifiers | ModNoRepeat, virtualKey);
        if (!_registered)
        {
            error = "이미 다른 프로그램이 같은 단축키를 사용 중일 수 있습니다.";
        }

        return _registered;
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private void Unregister()
    {
        if (_registered && _handle != IntPtr.Zero)
        {
            UnregisterHotKey(_handle, _hotkeyId);
        }

        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == _hotkeyId)
        {
            handled = true;
            _callback?.Invoke();
        }

        return IntPtr.Zero;
    }

    private static bool TryParseGesture(string text, out uint modifiers, out uint virtualKey, out string? error)
    {
        modifiers = 0;
        virtualKey = 0;
        error = null;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            error = "단축키는 Ctrl+Alt+A처럼 보조키와 일반 키를 함께 입력해야 합니다.";
            return false;
        }

        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift":
                    modifiers |= ModShift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModWin;
                    break;
                default:
                    error = $"알 수 없는 보조키입니다: {parts[i]}";
                    return false;
            }
        }

        try
        {
            var converter = new KeyConverter();
            var key = (Key?)converter.ConvertFromString(parts[^1]);
            if (key is null || key == Key.None)
            {
                error = "일반 키를 인식하지 못했습니다.";
                return false;
            }

            virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
            return true;
        }
        catch (Exception ex) when (ex is NotSupportedException or FormatException)
        {
            error = $"일반 키를 인식하지 못했습니다: {parts[^1]}";
            return false;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
