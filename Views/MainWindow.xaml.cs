using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MiniSoundpad.Audio;
using MiniSoundpad.Models;
using MiniSoundpad.Services;

namespace MiniSoundpad.Views;

public partial class MainWindow : Window
{
    private const string AllCategories = "（全部）";
    private static readonly string[] AudioExtensions =
        { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac", ".aiff", ".aif", ".mp4" };

    private readonly ObservableCollection<SoundItem> _items = new();
    private readonly ICollectionView _view;
    private readonly AudioEngine _engine = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private AppSettings _settings = Storage.LoadSettings();
    private HotkeyManager? _hotkeys;
    private TrayIcon? _tray;
    private bool _reallyExit;        // true = 真的退出；false = 点 ✕ 只是缩到托盘
    private bool _trayHintShown;
    private string _hotkeyWarning = "";

    public MainWindow()
    {
        App.Log("创建主窗口");
        InitializeComponent();

        foreach (var item in Storage.LoadLibrary()) Track(item);
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = FilterItem;
        SoundList.ItemsSource = _view;

        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveLibrary(); };
        SoundList.SelectionChanged += (_, _) => UpdateTrayTooltip();
        // 关机/注销时别被"缩到托盘"挡住
        Application.Current.SessionEnding += (_, _) => _reallyExit = true;
        RefreshCategories();
        UpdateEmptyHint();
    }

    // ================= 生命周期 =================

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        App.Log("主窗口已加载，注册快捷键");
        _hotkeys = new HotkeyManager(this);
        try
        {
            _tray = new TrayIcon();
            _tray.ShowRequested += ShowFromTray;
            _tray.StopAllRequested += () => _engine.StopAll();
            _tray.SettingsRequested += () =>
            {
                ShowFromTray();
                if (OwnedWindows.Count == 0) Settings_Click(this, new RoutedEventArgs());
            };
            _tray.ExitRequested += ExitApp;
        }
        catch (Exception ex)
        {
            App.Log("托盘图标创建失败: " + ex);
        }
        App.Log("启动音频引擎");
        RestartAudio();
        App.Log($"音频引擎已启动：耳机={_engine.MonitorDeviceName ?? "无"}，虚拟麦={_engine.CableDeviceName ?? "无"}，麦克风={_engine.MicDeviceName ?? "无"}");
        foreach (var w in _engine.Warnings) App.Log("警告: " + w);
        _engine.Preload(_items.Select(i => i.FilePath));
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_reallyExit && _settings.CloseToTray && _tray != null)
        {
            // 点 ✕ 只是藏到托盘，快捷键和麦克风直通继续工作
            e.Cancel = true;
            SaveLibrary();
            Hide();
            if (!_trayHintShown)
            {
                _tray.ShowBalloon("Mini Soundpad 还在后台运行",
                    "快捷键和虚拟麦克风照常工作。点托盘里的耳机图标打开窗口，右键 → 退出 才会真正关闭。");
                _trayHintShown = true;
            }
            return;
        }

        App.Log("主窗口关闭");
        SaveLibrary();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _engine.Dispose();
    }

    /// <summary>从托盘恢复窗口（也给"再次双击快捷方式"用）。</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        // 有时 Activate 抢不到前台，临时置顶一下
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ExitApp()
    {
        _reallyExit = true;
        // 有对话框开着时先关掉它们，否则主窗口关不掉
        foreach (Window w in OwnedWindows.Cast<Window>().ToList()) w.Close();
        Close();
    }

    private void RestartAudio()
    {
        _engine.Start(_settings);
        RegisterHotkeys();
        UpdateStatus();
    }

    // ================= 播放 =================

    private void PlayItem(SoundItem item)
    {
        if (item.IsMissing)
        {
            ShowWarning($"找不到文件：{item.FilePath}");
            return;
        }
        try
        {
            _engine.Play(item, _settings.StopOthersOnPlay);
        }
        catch (Exception ex)
        {
            ShowWarning($"无法播放「{item.Name}」：{ex.Message}");
        }
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SoundItem item) PlayItem(item);
    }

    private void SoundList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 双击滑块/按钮时不触发播放
        if (e.OriginalSource is DependencyObject d && (FindAncestor<Slider>(d) != null || FindAncestor<ButtonBase>(d) != null))
            return;
        if (FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject)?.DataContext is SoundItem item)
            PlayItem(item);
    }

    private void SoundList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Selected().FirstOrDefault() is { } item) { PlayItem(item); e.Handled = true; }
        else if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
        else if (e.Key == Key.F2) { MenuRename_Click(sender, e); e.Handled = true; }
    }

    private void StopAll_Click(object sender, RoutedEventArgs e) => _engine.StopAll();

    // ================= 添加 =================

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "选择音效文件",
            Filter = "音频文件|" + string.Join(";", AudioExtensions.Select(x => "*" + x)) + "|所有文件|*.*"
        };
        if (dlg.ShowDialog(this) == true) AddFiles(dlg.FileNames);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        // 拖进来的是文件夹就把里面的音频都加进来
        var files = paths.SelectMany(p => Directory.Exists(p)
            ? Directory.EnumerateFiles(p, "*.*", SearchOption.AllDirectories)
            : new[] { p });
        AddFiles(files.Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())));
    }

    private void AddFiles(IEnumerable<string> files)
    {
        string category = CurrentCategory() ?? "";
        var failed = new List<string>();
        int added = 0;
        foreach (var f in files)
        {
            if (_items.Any(i => string.Equals(i.FilePath, f, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                _engine.GetOrLoad(f); // 顺便检查能不能解码
            }
            catch (Exception ex)
            {
                failed.Add($"{Path.GetFileName(f)}（{ex.Message}）");
                continue;
            }
            Track(new SoundItem { Name = Path.GetFileNameWithoutExtension(f), FilePath = f, Category = category });
            added++;
        }
        AfterLibraryChanged();
        if (failed.Count > 0)
            MessageBox.Show(this, "以下文件无法读取：\n" + string.Join("\n", failed.Take(15)), "添加音效",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        else if (added > 0)
            ShowWarning("");
    }

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RecordWindow(_engine) { Owner = this };
        if (ShowDialogWithoutHotkeys(dlg) != true || dlg.ResultPath == null) return;

        var name = InputDialog.Ask(this, "新音效", "给这段录音起个名字：", $"录音 {DateTime.Now:MM-dd HH:mm}") ;
        Track(new SoundItem
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(dlg.ResultPath) : name,
            FilePath = dlg.ResultPath,
            Category = CurrentCategory() ?? ""
        });
        AfterLibraryChanged();
    }

    // ================= 右键菜单 =================

    private void MenuPlay_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Selected()) PlayItem(item);
    }

    private void MenuHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (Selected().FirstOrDefault() is not { } item) return;
        var dlg = new HotkeyDialog($"音效：{item.Name}", item.Hotkey) { Owner = this };
        if (ShowDialogWithoutHotkeys(dlg) != true) return;

        // 同一个组合键只能给一个音效，先从别的音效上拿掉
        if (dlg.Result != null)
        {
            string key = dlg.Result.ToString();
            var clash = _settings.GlobalHotkeys().FirstOrDefault(g => g.Key.ToString() == key);
            if (clash.Name != null)
            {
                MessageBox.Show(this, $"{key} 已经被「{clash.Name}」占用了，请换一个，或在设置里先改掉它。", "快捷键");
                return;
            }
            foreach (var other in _items.Where(i => i != item && i.Hotkey?.ToString() == key))
                other.Hotkey = null;
        }
        item.Hotkey = dlg.Result;
        AfterLibraryChanged();
    }

    private void MenuRename_Click(object sender, RoutedEventArgs e)
    {
        if (Selected().FirstOrDefault() is not { } item) return;
        var name = InputDialog.Ask(this, "重命名", "新名称：", item.Name);
        if (!string.IsNullOrWhiteSpace(name)) { item.Name = name; AfterLibraryChanged(); }
    }

    private void MenuCategory_Click(object sender, RoutedEventArgs e)
    {
        var sel = Selected().ToList();
        if (sel.Count == 0) return;
        var cat = InputDialog.Ask(this, "设置分类", $"为 {sel.Count} 个音效设置分类（留空 = 无分类）：",
            sel[0].Category, Categories());
        if (cat == null) return;
        foreach (var i in sel) i.Category = cat;
        AfterLibraryChanged();
    }

    private void MenuTrim_Click(object sender, RoutedEventArgs e)
    {
        if (Selected().FirstOrDefault() is not { } item || item.IsMissing) return;
        try
        {
            var output = Storage.NewSoundPath(item.Name);
            var dlg = new TrimWindow(_engine, item.FilePath, output,
                $"剪辑「{item.Name}」——保存为新文件，原文件不会被修改") { Owner = this };
            if (ShowDialogWithoutHotkeys(dlg) == true && dlg.ResultPath != null)
            {
                _engine.Forget(item.FilePath);
                item.FilePath = dlg.ResultPath;
                AfterLibraryChanged();
            }
        }
        catch (Exception ex)
        {
            ShowWarning("无法打开剪辑：" + ex.Message);
        }
    }

    private void MenuReveal_Click(object sender, RoutedEventArgs e)
    {
        if (Selected().FirstOrDefault() is { } item && File.Exists(item.FilePath))
            Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    private void DeleteSelected()
    {
        var sel = Selected().ToList();
        if (sel.Count == 0) return;
        if (MessageBox.Show(this, $"从列表中删除 {sel.Count} 个音效？（不会删除硬盘上的文件）", "删除",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var i in sel)
        {
            _items.Remove(i);
            _engine.Forget(i.FilePath);
        }
        AfterLibraryChanged();
    }

    // ================= 设置 =================

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var copy = Storage.LoadSettings(); // 在副本上改，取消时不影响当前设置
        CopySettings(_settings, copy);
        var dlg = new SettingsWindow(copy) { Owner = this };
        if (ShowDialogWithoutHotkeys(dlg) != true) return;

        // 全局快捷键和某个音效撞键了：把音效上的去掉
        var globals = copy.GlobalHotkeys().Select(g => g.Key.ToString()).ToHashSet();
        foreach (var i in _items.Where(i => globals.Contains(i.HotkeyText))) i.Hotkey = null;

        _settings = copy;
        Storage.SaveSettings(_settings);
        SaveLibrary();
        RestartAudio();
    }

    private static void CopySettings(AppSettings from, AppSettings to)
    {
        foreach (var p in typeof(AppSettings).GetProperties().Where(p => p.CanWrite))
            p.SetValue(to, p.GetValue(from));
    }

    // ================= 快捷键 =================

    private void RegisterHotkeys()
    {
        if (_hotkeys == null) return;
        _hotkeys.UnregisterAll();
        var failed = new List<string>();

        void Global(Hotkey? hk, string name, Action action)
        {
            if (hk != null && !_hotkeys!.Register(hk, action)) failed.Add($"{hk}（{name}）");
        }
        Global(_settings.StopAllHotkey, "全部停止", () => _engine.StopAll());
        Global(_settings.SelectPrevHotkey, "选上一个", () => SelectRelative(-1));
        Global(_settings.SelectNextHotkey, "选下一个", () => SelectRelative(+1));
        Global(_settings.PlaySelectedHotkey, "播放选中", PlaySelected);

        foreach (var item in _items.Where(i => i.Hotkey != null))
        {
            var it = item;
            if (!_hotkeys.Register(it.Hotkey!, () => PlayItem(it)))
                failed.Add($"{it.Hotkey}（{it.Name}）");
        }

        StopAllButton.ToolTip = _settings.StopAllHotkey != null ? $"快捷键：{_settings.StopAllHotkey}" : null;
        _hotkeyWarning = failed.Count == 0 ? "" : "这些快捷键被其它程序占用，注册失败：" + string.Join("、", failed);
    }

    // ================= 用快捷键选音效 =================

    /// <summary>在当前显示的列表（受搜索/分类过滤影响）里上下移动选中项，到头会绕回来。</summary>
    private void SelectRelative(int delta)
    {
        var list = _view.Cast<SoundItem>().ToList();
        if (list.Count == 0) return;
        int idx = SoundList.SelectedItem is SoundItem cur ? list.IndexOf(cur) : -1;
        idx = idx < 0 ? (delta > 0 ? 0 : list.Count - 1) : (idx + delta + list.Count) % list.Count;

        var item = list[idx];
        SoundList.SelectedItem = item;
        SoundList.ScrollIntoView(item);
        UpdateTrayTooltip();

        if (_settings.PreviewOnSelect && !item.IsMissing)
        {
            try
            {
                var snd = _engine.GetOrLoad(item.FilePath);
                int len = Math.Min(snd.Data.Length, snd.SampleRate * snd.Channels * 3 / 2); // 1.5 秒
                _engine.Preview(snd, 0, len);
            }
            catch { /* 试听失败不影响选择 */ }
        }
    }

    private void PlaySelected()
    {
        if (SoundList.SelectedItem is SoundItem item)
        {
            _engine.StopPreview();
            PlayItem(item);
        }
    }

    private void UpdateTrayTooltip()
    {
        var name = (SoundList.SelectedItem as SoundItem)?.Name;
        _tray?.SetTooltip(name == null ? "Mini Soundpad" : $"Mini Soundpad\n选中：{name}");
    }

    /// <summary>弹设置快捷键之类的对话框时先暂停全局快捷键，否则按下已注册的组合键会被拦截。</summary>
    private bool? ShowDialogWithoutHotkeys(Window dlg)
    {
        _hotkeys?.UnregisterAll();
        try { return dlg.ShowDialog(); }
        finally { RegisterHotkeys(); UpdateStatus(); }
    }

    // ================= 列表 / 分类 / 过滤 =================

    private void Track(SoundItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SoundItem.Volume)) { _saveTimer.Stop(); _saveTimer.Start(); }
        };
        _items.Add(item);
    }

    private void AfterLibraryChanged()
    {
        SaveLibrary();
        RefreshCategories();
        _view.Refresh();
        RegisterHotkeys();
        UpdateStatus();
        UpdateEmptyHint();
    }

    private void SaveLibrary()
    {
        try { Storage.SaveLibrary(_items); }
        catch (Exception ex) { ShowWarning("保存音效列表失败：" + ex.Message); }
    }

    private IEnumerable<SoundItem> Selected() => SoundList.SelectedItems.Cast<SoundItem>();

    private List<string> Categories() =>
        _items.Select(i => i.Category).Where(c => !string.IsNullOrWhiteSpace(c))
              .Distinct().OrderBy(c => c).ToList();

    private string? CurrentCategory() =>
        CategoryBox.SelectedItem is string s && s != AllCategories ? s : null;

    private void RefreshCategories()
    {
        var current = CategoryBox.SelectedItem as string;
        var list = new List<string> { AllCategories };
        list.AddRange(Categories());
        CategoryBox.ItemsSource = list;
        CategoryBox.SelectedItem = current != null && list.Contains(current) ? current : AllCategories;
    }

    private bool FilterItem(object o)
    {
        if (o is not SoundItem i) return false;
        var cat = CurrentCategory();
        if (cat != null && i.Category != cat) return false;
        var q = SearchBox?.Text?.Trim();
        return string.IsNullOrEmpty(q)
               || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
               || i.Category.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => _view?.Refresh();

    private void UpdateEmptyHint() =>
        EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ================= 状态显示 =================

    private void UpdateStatus()
    {
        StatusText.Text =
            $"🎧 耳机：{_engine.MonitorDeviceName ?? "无"}    " +
            $"🎙 虚拟麦：{_engine.CableDeviceName ?? "未启用"}    " +
            $"麦克风直通：{(_engine.MicDeviceName != null ? _engine.MicDeviceName : "关")}    " +
            $"音效数：{_items.Count}";
        ShowWarning(string.Join("\n", _engine.Warnings.Append(_hotkeyWarning).Where(w => w.Length > 0)));
    }

    private void ShowWarning(string text)
    {
        WarningText.Text = text;
        WarningText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }
}
