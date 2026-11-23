using System.Windows;
using System.Windows.Controls;
using MiniSoundpad.Audio;
using MiniSoundpad.Models;
using NAudio.CoreAudioApi;

namespace MiniSoundpad.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _s;

    // 四个全局快捷键：tag -> (显示名, 当前值, 显示用的 TextBlock)
    private readonly Dictionary<string, (string Name, Hotkey? Key, TextBlock Text)> _keys;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _s = settings;

        var render = AudioUtil.ListDevices(DataFlow.Render);
        var capture = AudioUtil.ListDevices(DataFlow.Capture);

        MonitorBox.ItemsSource = new[] { new DeviceInfo("", "（系统默认播放设备）") }.Concat(render).ToList();
        CableBox.ItemsSource = new[]
        {
            new DeviceInfo("", "（自动检测 VB-Cable）"),
            new DeviceInfo(AppSettings.NoDevice, "（不使用虚拟麦克风）")
        }.Concat(render).ToList();
        MicBox.ItemsSource = new[] { new DeviceInfo("", "（系统默认麦克风）") }
            .Concat(capture.Where(d => !AudioUtil.IsVirtualCable(d.Name))).ToList();

        SelectDevice(MonitorBox, _s.MonitorDeviceId);
        SelectDevice(CableBox, _s.CableDeviceId);
        SelectDevice(MicBox, _s.MicDeviceId);

        MicPassBox.IsChecked = _s.MicPassthrough;
        MonitorVol.Value = _s.MonitorVolume;
        CableVol.Value = _s.CableVolume;
        MicVol.Value = _s.MicVolume;
        StopOthersBox.IsChecked = _s.StopOthersOnPlay;
        PreviewOnSelectBox.IsChecked = _s.PreviewOnSelect;
        CloseToTrayBox.IsChecked = _s.CloseToTray;

        _keys = new()
        {
            ["stop"] = ("全部停止", _s.StopAllHotkey, StopKeyText),
            ["prev"] = ("选上一个音效", _s.SelectPrevHotkey, PrevKeyText),
            ["next"] = ("选下一个音效", _s.SelectNextHotkey, NextKeyText),
            ["play"] = ("播放选中的音效", _s.PlaySelectedHotkey, PlayKeyText),
        };
        foreach (var k in _keys.Values) k.Text.Text = k.Key?.ToString() ?? "（无）";
    }

    private static void SelectDevice(ComboBox box, string? id)
    {
        box.SelectedValue = id ?? "";
        if (box.SelectedIndex < 0) box.SelectedIndex = 0; // 之前选的设备被拔掉了
    }

    private void ChangeKey_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string tag) return;
        var (name, key, text) = _keys[tag];
        var d = new HotkeyDialog($"「{name}」", key) { Owner = this };
        if (d.ShowDialog() != true) return;

        if (d.Result != null)
        {
            var dup = _keys.FirstOrDefault(p => p.Key != tag && p.Value.Key?.ToString() == d.Result.ToString());
            if (dup.Key != null)
            {
                MessageBox.Show(this, $"{d.Result} 已经用在「{dup.Value.Name}」上了，换一个吧。", "快捷键");
                return;
            }
        }
        _keys[tag] = (name, d.Result, text);
        text.Text = d.Result?.ToString() ?? "（无）";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        static string? Id(ComboBox b) => b.SelectedValue is string s && s.Length > 0 ? s : null;

        _s.MonitorDeviceId = Id(MonitorBox);
        _s.CableDeviceId = Id(CableBox);
        _s.MicDeviceId = Id(MicBox);
        _s.MicPassthrough = MicPassBox.IsChecked == true;
        _s.MonitorVolume = MonitorVol.Value;
        _s.CableVolume = CableVol.Value;
        _s.MicVolume = MicVol.Value;
        _s.StopOthersOnPlay = StopOthersBox.IsChecked == true;
        _s.PreviewOnSelect = PreviewOnSelectBox.IsChecked == true;
        _s.CloseToTray = CloseToTrayBox.IsChecked == true;
        _s.StopAllHotkey = _keys["stop"].Key;
        _s.SelectPrevHotkey = _keys["prev"].Key;
        _s.SelectNextHotkey = _keys["next"].Key;
        _s.PlaySelectedHotkey = _keys["play"].Key;
        DialogResult = true;
    }
}
