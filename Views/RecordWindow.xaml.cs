using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using MiniSoundpad.Audio;
using MiniSoundpad.Services;

namespace MiniSoundpad.Views;

/// <summary>录音 → 自动打开剪辑窗口 → 返回最终文件路径。</summary>
public partial class RecordWindow : Window
{
    private readonly AudioEngine _engine;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _clock = new();
    private Recorder? _recorder;
    private float _level;

    /// <summary>成功时为最终保存的 WAV 路径。</summary>
    public string? ResultPath { get; private set; }

    public RecordWindow(AudioEngine engine)
    {
        InitializeComponent();
        _engine = engine;
        SourceBox.ItemsSource = Recorder.ListSources();
        SourceBox.SelectedIndex = 0;

        _timer.Tick += (_, _) =>
        {
            TimeText.Text = _clock.Elapsed.ToString(@"mm\:ss\.f");
            LevelBar.Value = _level;
            _level *= 0.7f; // 电平表缓慢回落
        };
    }

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder == null) StartRecording();
        else _recorder.Stop(); // 结束后在 OnStopped 里处理
    }

    private void StartRecording()
    {
        if (SourceBox.SelectedItem is not RecordSource src) return;
        var rawPath = Storage.NewSoundPath("rec_raw");
        _recorder = new Recorder(rawPath);
        _recorder.LevelChanged += l => _level = Math.Max(_level, l);
        _recorder.RecordingStopped += err => Dispatcher.InvokeAsync(() => OnStopped(rawPath, err));
        try
        {
            _recorder.Start(src);
        }
        catch (Exception ex)
        {
            _recorder.Dispose();
            _recorder = null;
            MessageBox.Show(this, "无法开始录音：" + ex.Message, "录音");
            return;
        }
        _clock.Restart();
        _timer.Start();
        RecordButton.Content = "■ 停止";
        SourceBox.IsEnabled = false;
        CloseButton.IsEnabled = false;
    }

    private void OnStopped(string rawPath, Exception? error)
    {
        _timer.Stop();
        _clock.Stop();
        _recorder?.Dispose();
        _recorder = null;
        RecordButton.Content = "● 开始录音";
        SourceBox.IsEnabled = true;
        CloseButton.IsEnabled = true;
        LevelBar.Value = 0;

        if (error != null)
        {
            MessageBox.Show(this, "录音出错：" + error.Message, "录音");
            TryDelete(rawPath);
            return;
        }
        if (!File.Exists(rawPath) || new FileInfo(rawPath).Length < 1000)
        {
            MessageBox.Show(this, "没有录到声音。", "录音");
            TryDelete(rawPath);
            return;
        }

        var finalPath = Storage.NewSoundPath("rec");
        bool? trimmed;
        try
        {
            var trim = new TrimWindow(_engine, rawPath, finalPath, "剪掉录音前后多余的部分，然后保存") { Owner = this };
            trimmed = trim.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "打开剪辑窗口失败：" + ex.Message, "录音");
            trimmed = false;
        }

        if (trimmed == true)
        {
            TryDelete(rawPath);
            ResultPath = finalPath;
            DialogResult = true;
        }
        else if (MessageBox.Show(this, "不剪辑，直接保存原始录音吗？", "录音",
                     MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            File.Move(rawPath, finalPath);
            ResultPath = finalPath;
            DialogResult = true;
        }
        else
        {
            TryDelete(rawPath);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_recorder != null) e.Cancel = true; // 先停止录音再关
    }
}
