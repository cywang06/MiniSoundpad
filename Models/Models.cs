using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace MiniSoundpad.Models;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>一个全局快捷键：修饰键 + 主键。</summary>
public sealed class Hotkey
{
    public ModifierKeys Modifiers { get; set; }
    public Key Key { get; set; }

    public Hotkey() { }
    public Hotkey(ModifierKeys modifiers, Key key) { Modifiers = modifiers; Key = key; }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    private static string KeyName(Key k)
    {
        if (k >= Key.D0 && k <= Key.D9) return ((int)(k - Key.D0)).ToString();
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return "Num" + (int)(k - Key.NumPad0);
        return k switch
        {
            Key.Add => "Num+",
            Key.Subtract => "Num-",
            Key.Multiply => "Num*",
            Key.Divide => "Num/",
            Key.Decimal => "Num.",
            Key.PageUp => "PageUp",      // 这几个键在 WPF 里有重名（Prior/Next/Return/Capital）
            Key.PageDown => "PageDown",
            Key.Enter => "Enter",
            Key.CapsLock => "CapsLock",
            _ => k.ToString()
        };
    }
}

public sealed class SoundItem : ObservableObject
{
    private string _name = "";
    private string _filePath = "";
    private string _category = "";
    private double _volume = 1.0;
    private Hotkey? _hotkey;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get => _name; set => Set(ref _name, value); }

    public string FilePath
    {
        get => _filePath;
        set { if (Set(ref _filePath, value)) Raise(nameof(IsMissing)); }
    }

    public string Category { get => _category; set => Set(ref _category, value ?? ""); }

    public double Volume { get => _volume; set => Set(ref _volume, Math.Clamp(value, 0, 1)); }

    public Hotkey? Hotkey
    {
        get => _hotkey;
        set { _hotkey = value; Raise(); Raise(nameof(HotkeyText)); }
    }

    [JsonIgnore] public string HotkeyText => Hotkey?.ToString() ?? "";
    [JsonIgnore] public bool IsMissing => !File.Exists(FilePath);
}

public sealed class AppSettings
{
    /// <summary>耳机/扬声器（自己听）。空 = 系统默认。</summary>
    public string? MonitorDeviceId { get; set; }

    /// <summary>虚拟线缆输入端（例如 "CABLE Input"）。空 = 自动检测，"none" = 不使用。</summary>
    public string? CableDeviceId { get; set; }

    /// <summary>真实麦克风。空 = 系统默认。</summary>
    public string? MicDeviceId { get; set; }

    public bool MicPassthrough { get; set; } = true;
    public double MicVolume { get; set; } = 1.0;
    public double MonitorVolume { get; set; } = 0.8;
    public double CableVolume { get; set; } = 1.0;
    public bool StopOthersOnPlay { get; set; } = false;
    public Hotkey? StopAllHotkey { get; set; } = new Hotkey(ModifierKeys.None, Key.Pause);

    // ---- 用快捷键在列表里选音效（游戏里不用切出去也能挑音效）----
    public Hotkey? SelectPrevHotkey { get; set; } = new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.PageUp);
    public Hotkey? SelectNextHotkey { get; set; } = new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.PageDown);
    public Hotkey? PlaySelectedHotkey { get; set; } = new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.Home);

    /// <summary>用快捷键切换选中时，在耳机里试听开头一小段（别人听不到），方便盲选。</summary>
    public bool PreviewOnSelect { get; set; } = true;

    /// <summary>点窗口的 X 时缩到托盘而不是退出。</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>所有"全局"快捷键（不属于某个音效的），用于检查冲突。</summary>
    public IEnumerable<(string Name, Hotkey Key)> GlobalHotkeys()
    {
        if (StopAllHotkey != null) yield return ("全部停止", StopAllHotkey);
        if (SelectPrevHotkey != null) yield return ("选上一个", SelectPrevHotkey);
        if (SelectNextHotkey != null) yield return ("选下一个", SelectNextHotkey);
        if (PlaySelectedHotkey != null) yield return ("播放选中", PlaySelectedHotkey);
    }

    public const string NoDevice = "none";
}
