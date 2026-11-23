using System.Windows;
using System.Windows.Threading;
using MiniSoundpad.Audio;
using NAudio.Wave;

namespace MiniSoundpad.Views;

/// <summary>
/// 裁剪一个音频文件，结果另存为新的 WAV（原文件不动），保存路径由调用方给出。
/// </summary>
public partial class TrimWindow : Window
{
    private readonly AudioEngine _engine;
    private readonly CachedSound _sound;
    private readonly string _outputPath;
    private readonly DispatcherTimer _timer;
    private Voice? _preview;
    private bool _syncing;

    public string? ResultPath { get; private set; }

    public TrimWindow(AudioEngine engine, string sourcePath, string outputPath, string header)
    {
        InitializeComponent();
        _engine = engine;
        _outputPath = outputPath;
        HeaderText.Text = header;

        _sound = new CachedSound(sourcePath);
        Wave.SetSamples(_sound.Data, _sound.Channels);

        double dur = _sound.DurationSeconds;
        StartSlider.Maximum = dur;
        EndSlider.Maximum = dur;
        StartSlider.Value = 0;
        EndSlider.Value = dur;
        UpdateLabels();

        Wave.EdgeDragged += (isStart, f) =>
        {
            if (isStart) StartSlider.Value = f * dur;
            else EndSlider.Value = f * dur;
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += (_, _) =>
        {
            if (_preview == null || _preview.Finished) { Wave.Playhead = -1; _preview = null; _timer.Stop(); return; }
            Wave.Playhead = _preview.Position / (double)_sound.Data.Length;
        };
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || EndSlider == null || StartSlider == null || Wave == null) return;
        _syncing = true;
        const double minLen = 0.05;
        if (sender == StartSlider && StartSlider.Value > EndSlider.Value - minLen)
            StartSlider.Value = Math.Max(0, EndSlider.Value - minLen);
        if (sender == EndSlider && EndSlider.Value < StartSlider.Value + minLen)
            EndSlider.Value = Math.Min(EndSlider.Maximum, StartSlider.Value + minLen);
        _syncing = false;
        UpdateLabels();
    }

    private void UpdateLabels()
    {
        double dur = Math.Max(_sound.DurationSeconds, 1e-6);
        Wave.Start = StartSlider.Value / dur;
        Wave.End = EndSlider.Value / dur;
        StartText.Text = $"{StartSlider.Value:0.00}s";
        EndText.Text = $"{EndSlider.Value:0.00}s";
        LengthText.Text = $"选区长度 {EndSlider.Value - StartSlider.Value:0.00}s / 总长 {_sound.DurationSeconds:0.00}s";
    }

    private (int start, int end) SelectionSamples()
    {
        int ch = _sound.Channels, rate = _sound.SampleRate;
        int s = (int)(StartSlider.Value * rate) * ch;
        int e = (int)(EndSlider.Value * rate) * ch;
        return (Math.Clamp(s, 0, _sound.Data.Length), Math.Clamp(e, 0, _sound.Data.Length));
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        var (s, en) = SelectionSamples();
        _preview = _engine.Preview(_sound, s, en);
        if (_preview == null)
            MessageBox.Show(this, "没有可用的耳机输出，无法试听。", "试听");
        else
            _timer.Start();
    }

    private void StopPreview_Click(object sender, RoutedEventArgs e) => _engine.StopPreview();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _engine.StopPreview();
        var (s, en) = SelectionSamples();
        int ch = _sound.Channels, rate = _sound.SampleRate;
        int frames = (en - s) / ch;
        if (frames < rate / 20) { MessageBox.Show(this, "选区太短了。", "剪辑"); return; }

        var buf = new float[frames * ch];
        Array.Copy(_sound.Data, s, buf, 0, buf.Length);

        // 首尾各 5ms 淡入淡出，避免剪切点出现"啪"的爆音
        int fade = Math.Min(rate * 5 / 1000, frames / 2);
        for (int i = 0; i < fade; i++)
        {
            float g = i / (float)fade;
            for (int c = 0; c < ch; c++)
            {
                buf[i * ch + c] *= g;
                buf[(frames - 1 - i) * ch + c] *= g;
            }
        }
        for (int i = 0; i < buf.Length; i++) buf[i] = Math.Clamp(buf[i], -1f, 1f);

        using (var w = new WaveFileWriter(_outputPath, new WaveFormat(rate, 16, ch)))
            w.WriteSamples(buf, 0, buf.Length);

        ResultPath = _outputPath;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_Closed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _engine.StopPreview();
    }
}
