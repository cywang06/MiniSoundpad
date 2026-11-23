using NAudio.Wave;

namespace MiniSoundpad.Audio;

/// <summary>
/// 整个音频文件预先解码进内存（48k 立体声 float）。
/// 同一份数据可以同时喂给多个输出（耳机 + 虚拟麦克风），触发时没有解码延迟。
/// </summary>
public sealed class CachedSound
{
    public float[] Data { get; }
    public int Channels => AudioUtil.MixFormat.Channels;
    public int SampleRate => AudioUtil.MixFormat.SampleRate;
    public double DurationSeconds => Data.Length / (double)Channels / SampleRate;

    public CachedSound(string path)
    {
        // AudioFileReader 支持 wav/mp3/aiff，其它格式（m4a/wma/flac/aac）走 Windows Media Foundation
        using var reader = new AudioFileReader(path);
        var sp = AudioUtil.ToMixFormat(reader);

        var all = new List<float>((int)Math.Min(reader.Length / 4 + 1024, 20_000_000));
        var buf = new float[SampleRate * Channels];
        int n;
        while ((n = sp.Read(buf, 0, buf.Length)) > 0)
            all.AddRange(new ArraySegment<float>(buf, 0, n));
        Data = all.ToArray();
    }
}

/// <summary>一次播放（一个"声部"）。读完或被 Stop() 后返回 0，混音器会自动把它移除。</summary>
public sealed class Voice : ISampleProvider
{
    private readonly float[] _data;
    private readonly int _end;
    private int _pos;
    private volatile bool _stopped;

    public string Tag { get; }
    public float Volume { get; set; }
    public WaveFormat WaveFormat => AudioUtil.MixFormat;
    public int Position => _pos;
    public bool Finished { get; private set; }

    public Voice(string tag, CachedSound sound, float volume, int startSample = 0, int endSample = -1)
    {
        Tag = tag;
        _data = sound.Data;
        _end = endSample < 0 ? _data.Length : Math.Min(endSample, _data.Length);
        _pos = Math.Clamp(startSample, 0, _end);
        Volume = volume;
    }

    public void Stop() => _stopped = true;

    public int Read(float[] buffer, int offset, int count)
    {
        if (_stopped) { Finished = true; return 0; }
        int n = Math.Min(count, _end - _pos);
        float v = Volume;
        for (int i = 0; i < n; i++)
            buffer[offset + i] = _data[_pos + i] * v;
        _pos += n;
        if (n < count) Finished = true;
        return n;
    }
}
