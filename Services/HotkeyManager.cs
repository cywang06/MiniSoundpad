using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using MiniSoundpad.Models;

namespace MiniSoundpad.Services;

/// <summary>
/// 用 Win32 RegisterHotKey 注册系统级快捷键——窗口在后台、你在打游戏时也能触发。
/// 注意：注册后这个组合键会被本程序"独占"，其它程序收不到。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000; // 按住不放不会连发

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyManager(Window window)
    {
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source.AddHook(WndProc);
    }

    /// <returns>false = 该组合键已被其它程序（或本程序里另一个音效）占用</returns>
    public bool Register(Hotkey hk, Action action)
    {
        int id = _nextId++;
        // WPF 的 ModifierKeys 数值（Alt=1, Ctrl=2, Shift=4, Win=8）正好等于 Win32 的 MOD_* 值
        uint mods = (uint)hk.Modifiers | MOD_NOREPEAT;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(hk.Key);
        if (!RegisterHotKey(_hwnd, id, mods, vk)) return false;
        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_hwnd, id);
        _actions.Clear();
        _nextId = 1;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }
}
