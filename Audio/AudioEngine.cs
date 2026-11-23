using System.IO;
using System.Collections.Concurrent;
using MiniSoundpad.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MiniSoundpad.Audio;

/// <summary>
/// 信号流：
///
///   音效 ──┬──► [耳机混音器] ──► 耳机/扬声器        （你自己听）
///          │
///          └──► [线缆混音器] ──► CABLE Input ──► CABLE Output = Discord/游戏里选的"麦克风"
///                    ▲
///   真实麦克风 ──────┘ （麦克风直通，这样别人既能听到你说话又能听到音效）
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private const string PreviewTag = "__preview__";

    private readonly object _lock = new();
    private readonly List<Voice> _voices = new();
    private readonly ConcurrentDictionary<string, CachedSound> _cache = new(StringComparer.OrdinalIgnoreCase);

    private MMDeviceEnumerator? _enumerator;
    private MixingSampleProvider? _monitorMixer, _cableMixer;
    private VolumeSampleProvider? _monitorVol, _cableVol, _micVol;
    private WasapiOut? _monitorOut, _cableOut;
    private WasapiCapture? _micCapture;
    private BufferedWaveProvider? _micBuffer;

    public string? MonitorDeviceName { get; private set; }
    public string? CableDeviceName { get; private set; }
    public string? MicDeviceName { get; private set; }
    public List<string> Warnings { get; } = new();

    public void Start(AppSettings s)
    {
        Shutdown();
        Warnings.Clear();
        _enumerator = new MMDeviceEnumerator();

        // ---- 1. 耳机（自己听）----
        var monitor = AudioUtil.FindById(_enumerator, s.MonitorDeviceId)
                      ?? AudioUtil.GetDefault(_enumerator, DataFlow.Render);

        // ---- 2. 虚拟线缆 ----
        MMDevice? cable = null;
        if (s.CableDeviceId != AppSettings.NoDevice)
        {
            cable = AudioUtil.FindById(_enumerator, s.CableDeviceId) ?? AudioUtil.FindCableInput(_enumerator);
            if (cable == null)
                Warnings.Add("没找到 VB-Cable 虚拟线缆，音效只会在本机播放。请安装 VB-Audio Virtual Cable。");
        }

        if (monitor != null && cable != null && monitor.ID == cable.ID)
        {
            Warnings.Add("耳机输出和虚拟线缆是同一个设备，已忽略耳机输出。请在设置里选真正的耳机。");
            monitor = null;
        }

        if (monitor != null)
        {
            try
            {
                _monitorMixer = new MixingSampleProvider(AudioUtil.MixFormat) { ReadFully = true };
                _monitorVol = new VolumeSampleProvider(_monitorMixer) { Volume = (float)s.MonitorVolume };
                _monitorOut = new WasapiOut(monitor, AudioClientShareMode.Shared, true, 50);
                _monitorOut.Init(_monitorVol);
                _monitorOut.Play();
                MonitorDeviceName = monitor.FriendlyName;
            }
            catch (Exception ex)
            {
                Warnings.Add($"打开耳机输出失败：{ex.Message}");
                DisposeMonitor();
            }
        }

        if (cable != null)
        {
            try
            {
                _cableMixer = new MixingSampleProvider(AudioUtil.MixFormat) { ReadFully = true };
                _cableVol = new VolumeSampleProvider(_cableMixer) { Volume = (float)s.CableVolume };
                _cableOut = new WasapiOut(cable, AudioClientShareMode.Shared, true, 50);
                _cableOut.Init(_cableVol);
                _cableOut.Play();
                CableDeviceName = cable.FriendlyName;
            }
            catch (Exception ex)
            {
                Warnings.Add($"打开虚拟线缆失败：{ex.Message}");
                DisposeCable();
            }
        }

        // ---- 3. 麦克风直通（混进虚拟线缆）----
        if (_cableMixer != null && s.MicPassthrough)
        {
            var mic = AudioUtil.FindById(_enumerator, s.MicDeviceId) ?? AudioUtil.FindRealMic(_enumerator);
            if (mic == null)
                Warnings.Add("没找到麦克风，麦克风直通已关闭。");
            else
                StartMic(mic, (float)s.MicVolume);
        }
    }

    private void StartMic(MMDevice mic, float volume)
    {
        try
        {
            _micCapture = new WasapiCapture(mic, true, 20);
            var fmt = AudioUtil.Standardize(_micCapture.WaveFormat);
            var buffer = new BufferedWaveProvider(fmt)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromMilliseconds(500),
                ReadFully = true // 没数据时输出静音，保证混音器不把它移除
            };
            _micBuffer = buffer;

            _micCapture.DataAvailable += (_, e) =>
            {
                // 采集和播放是两个独立时钟，长时间运行会累积延迟；超过 200ms 就丢掉旧数据
                if (buffer.BufferedDuration.TotalMilliseconds > 200) buffer.ClearBuffer();
                buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            };

            var sp = AudioUtil.ToMixFormat(buffer.ToSampleProvider());
            _micVol = new VolumeSampleProvider(sp) { Volume = volume };
            _cableMixer!.AddMixerInput(_micVol);
            _micCapture.StartRecording();
            MicDeviceName = mic.FriendlyName;
        }
        catch (Exception ex)
        {
            Warnings.Add($"打开麦克风失败：{ex.Message}");
            DisposeMic();
        }
    }

    // ---------- 播放 ----------

    public CachedSound GetOrLoad(string path) => _cache.GetOrAdd(path, p => new CachedSound(p));

    public void Forget(string path) => _cache.TryRemove(path, out _);

    /// <summary>后台预解码，第一次按快捷键时不会卡。</summary>
    public void Preload(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        Task.Run(() =>
        {
            foreach (var p in list)
            {
                try { if (File.Exists(p)) GetOrLoad(p); } catch { /* 播放时再报错 */ }
            }
        });
    }

    public void Play(SoundItem item, bool stopOthers)
    {
        var snd = GetOrLoad(item.FilePath);
        lock (_lock)
        {
            if (stopOthers) StopWhere(_ => true);
            else StopWhere(v => v.Tag == item.Id); // 再按一次同一个音效 = 从头播放

            AddVoice(_monitorMixer, new Voice(item.Id, snd, (float)item.Volume));
            AddVoice(_cableMixer, new Voice(item.Id, snd, (float)item.Volume));
        }
    }

    /// <summary>只在耳机里试听（剪辑窗口用），不会送进麦克风。</summary>
    public Voice? Preview(CachedSound snd, int startSample, int endSample)
    {
        lock (_lock)
        {
            StopWhere(v => v.Tag == PreviewTag);
            if (_monitorMixer == null) return null;
            var v = new Voice(PreviewTag, snd, 1f, startSample, endSample);
            AddVoice(_monitorMixer, v);
            return v;
        }
    }

    public void StopPreview()
    {
        lock (_lock) StopWhere(v => v.Tag == PreviewTag);
    }

    public void StopAll()
    {
        lock (_lock) StopWhere(v => v.Tag != PreviewTag);
    }

    private void AddVoice(MixingSampleProvider? mixer, Voice v)
    {
        if (mixer == null) return;
        _voices.Add(v);
        mixer.AddMixerInput(v);
    }

    private void StopWhere(Func<Voice, bool> predicate)
    {
        _voices.RemoveAll(v => v.Finished);
        foreach (var v in _voices.Where(predicate)) v.Stop();
    }

    public void SetVolumes(double monitor, double cable, double mic)
    {
        if (_monitorVol != null) _monitorVol.Volume = (float)monitor;
        if (_cableVol != null) _cableVol.Volume = (float)cable;
        if (_micVol != null) _micVol.Volume = (float)mic;
    }

    // ---------- 清理 ----------

    private void DisposeMonitor()
    {
        try { _monitorOut?.Stop(); _monitorOut?.Dispose(); } catch { }
        _monitorOut = null; _monitorMixer = null; _monitorVol = null; MonitorDeviceName = null;
    }

    private void DisposeCable()
    {
        try { _cableOut?.Stop(); _cableOut?.Dispose(); } catch { }
        _cableOut = null; _cableMixer = null; _cableVol = null; CableDeviceName = null;
    }

    private void DisposeMic()
    {
        try { _micCapture?.StopRecording(); _micCapture?.Dispose(); } catch { }
        _micCapture = null; _micBuffer = null; _micVol = null; MicDeviceName = null;
    }

    private void Shutdown()
    {
        lock (_lock) { foreach (var v in _voices) v.Stop(); _voices.Clear(); }
        DisposeMic();
        DisposeMonitor();
        DisposeCable();
        _enumerator?.Dispose();
        _enumerator = null;
    }

    public void Dispose() => Shutdown();
}
