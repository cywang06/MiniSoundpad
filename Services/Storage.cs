using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniSoundpad.Models;

namespace MiniSoundpad.Services;

/// <summary>所有数据存在 %AppData%\MiniSoundpad\ 下。</summary>
public static class Storage
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniSoundpad");

    /// <summary>录音和剪辑生成的文件放这里。</summary>
    public static readonly string SoundsDir = Path.Combine(DataDir, "Sounds");

    private static readonly string LibraryFile = Path.Combine(DataDir, "library.json");
    private static readonly string SettingsFile = Path.Combine(DataDir, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文不转义，方便手动查看
        Converters = { new JsonStringEnumConverter() }
    };

    static Storage()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(SoundsDir);
    }

    public static List<SoundItem> LoadLibrary() => Load<List<SoundItem>>(LibraryFile) ?? new();
    public static void SaveLibrary(IEnumerable<SoundItem> items) => Save(LibraryFile, items.ToList());

    public static AppSettings LoadSettings() => Load<AppSettings>(SettingsFile) ?? new();
    public static void SaveSettings(AppSettings s) => Save(SettingsFile, s);

    public static string NewSoundPath(string prefix)
    {
        string safe = string.Concat(prefix.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe)) safe = "sound";
        return Path.Combine(SoundsDir, $"{safe}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.wav");
    }

    private static T? Load<T>(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : default;
        }
        catch
        {
            // 文件损坏：备份一份，然后用默认值启动
            try { File.Copy(path, path + ".broken", true); } catch { }
            return default;
        }
    }

    private static void Save<T>(string path, T value)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, true); // 先写临时文件再替换，避免写一半断电导致文件损坏
    }
}
