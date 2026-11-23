using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MiniSoundpad.Audio;

public sealed record DeviceInfo(string Id, string Name);

public static class AudioUtil
{
    /// <summary>内部统一的混音格式：48 kHz / 立体声 / 32-bit float。</summary>
    public static readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    private static readonly Guid SubTypePcm = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid SubTypeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>WASAPI 返回的通常是 WaveFormatExtensible，这里转成普通 PCM/Float 格式，方便 NAudio 的转换器处理。</summary>
    public static WaveFormat Standardize(WaveFormat f)
    {
        if (f is WaveFormatExtensible ext)
        {
            if (ext.SubFormat == SubTypeFloat)
                return WaveFormat.CreateIeeeFloatWaveFormat(f.SampleRate, f.Channels);
            if (ext.SubFormat == SubTypePcm)
                return new WaveFormat(f.SampleRate, f.BitsPerSample, f.Channels);
        }
        return f;
    }

    /// <summary>把任意采样率/声道数的音源转成 MixFormat。</summary>
    public static ISampleProvider ToMixFormat(ISampleProvider sp)
    {
        if (sp.WaveFormat.Channels == 1)
            sp = new MonoToStereoSampleProvider(sp);
        else if (sp.WaveFormat.Channels > 2)
            sp = new FirstTwoChannels(sp);

        if (sp.WaveFormat.SampleRate != MixFormat.SampleRate)
            sp = new WdlResamplingSampleProvider(sp, MixFormat.SampleRate);

        return sp;
    }

    // ---------- 设备枚举 ----------

    public static List<DeviceInfo> ListDevices(DataFlow flow)
    {
        using var en = new MMDeviceEnumerator();
        return en.EnumerateAudioEndPoints(flow, DeviceState.Active)
                 .Select(d => new DeviceInfo(d.ID, d.FriendlyName))
                 .ToList();
    }

    public static MMDevice? FindById(MMDeviceEnumerator en, string? id)
    {
        if (string.IsNullOrEmpty(id) || id == Models.AppSettings.NoDevice) return null;
        try
        {
            var d = en.GetDevice(id);
            return d.State == DeviceState.Active ? d : null;
        }
        catch { return null; }
    }

    public static MMDevice? GetDefault(MMDeviceEnumerator en, DataFlow flow)
    {
        try { return en.GetDefaultAudioEndpoint(flow, Role.Multimedia); }
        catch { return null; }
    }

    public static bool IsVirtualCable(string name) =>
        name.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VoiceMeeter", StringComparison.OrdinalIgnoreCase);

    /// <summary>自动找 VB-Cable 的播放端 "CABLE Input (VB-Audio Virtual Cable)"。</summary>
    public static MMDevice? FindCableInput(MMDeviceEnumerator en) =>
        en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
          .FirstOrDefault(d => d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase))
        ?? en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
             .FirstOrDefault(d => IsVirtualCable(d.FriendlyName));

    /// <summary>默认麦克风；如果系统默认就是虚拟线缆，就换成第一个真实麦克风，避免回环。</summary>
    public static MMDevice? FindRealMic(MMDeviceEnumerator en)
    {
        var def = GetDefault(en, DataFlow.Capture);
        if (def != null && !IsVirtualCable(def.FriendlyName)) return def;
        return en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                 .FirstOrDefault(d => !IsVirtualCable(d.FriendlyName));
    }

    /// <summary>从 byte 缓冲区算峰值（用于电平表）。</summary>
    public static float Peak(byte[] buffer, int bytes, WaveFormat f)
    {
        float max = 0;
        if (f.Encoding == WaveFormatEncoding.IeeeFloat && f.BitsPerSample == 32)
        {
            for (int i = 0; i + 4 <= bytes; i += 4)
                max = Math.Max(max, Math.Abs(BitConverter.ToSingle(buffer, i)));
        }
        else if (f.BitsPerSample == 16)
        {
            for (int i = 0; i + 2 <= bytes; i += 2)
                max = Math.Max(max, Math.Abs(BitConverter.ToInt16(buffer, i) / 32768f));
        }
        return Math.Min(max, 1f);
    }
}

/// <summary>多声道（5.1 等）只取前两个声道。</summary>
internal sealed class FirstTwoChannels : ISampleProvider
{
    private readonly ISampleProvider _src;
    private readonly int _ch;
    private float[] _buf = Array.Empty<float>();

    public FirstTwoChannels(ISampleProvider src)
    {
        _src = src;
        _ch = src.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(src.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / 2;
        int need = frames * _ch;
        if (_buf.Length < need) _buf = new float[need];
        int read = _src.Read(_buf, 0, need);
        int gotFrames = read / _ch;
        for (int i = 0; i < gotFrames; i++)
        {
            buffer[offset + i * 2] = _buf[i * _ch];
            buffer[offset + i * 2 + 1] = _buf[i * _ch + 1];
        }
        return gotFrames * 2;
    }
}
