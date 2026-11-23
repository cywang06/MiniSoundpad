using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MiniSoundpad.Audio;

public sealed record RecordSource(string Id, string Name, bool Loopback);

/// <summary>从麦克风录音，或"内录"某个播放设备正在放的声音（WASAPI loopback）。</summary>
public sealed class Recorder : IDisposable
{
    private WasapiCapture? _capture;
    private WaveFileWriter? _writer;
    private WaveFormat? _format;

    public string FilePath { get; }
    public event Action<float>? LevelChanged;          // 来自音频线程
    public event Action<Exception?>? RecordingStopped;  // 来自音频线程

    public Recorder(string filePath) { FilePath = filePath; }

    public static List<RecordSource> ListSources()
    {
        var list = new List<RecordSource>();
        foreach (var d in AudioUtil.ListDevices(DataFlow.Capture))
            list.Add(new RecordSource(d.Id, "🎤 " + d.Name, false));
        foreach (var d in AudioUtil.ListDevices(DataFlow.Render))
            list.Add(new RecordSource(d.Id, "🔊 内录：" + d.Name, true));
        return list;
    }

    public void Start(RecordSource source)
    {
        using var en = new MMDeviceEnumerator();
        var dev = AudioUtil.FindById(en, source.Id)
                  ?? throw new InvalidOperationException("找不到录音设备：" + source.Name);

        _capture = source.Loopback ? new WasapiLoopbackCapture(dev) : new WasapiCapture(dev);
        _format = AudioUtil.Standardize(_capture.WaveFormat);
        _writer = new WaveFileWriter(FilePath, _format);

        _capture.DataAvailable += (_, e) =>
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
            LevelChanged?.Invoke(AudioUtil.Peak(e.Buffer, e.BytesRecorded, _format));
        };
        _capture.RecordingStopped += (_, e) =>
        {
            _writer?.Dispose();
            _writer = null;
            RecordingStopped?.Invoke(e.Exception);
        };
        _capture.StartRecording();
    }

    public void Stop() => _capture?.StopRecording();

    public void Dispose()
    {
        try { _capture?.StopRecording(); } catch { }
        _capture?.Dispose();
        _capture = null;
        _writer?.Dispose();
        _writer = null;
    }
}
