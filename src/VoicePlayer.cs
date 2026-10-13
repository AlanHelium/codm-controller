using System.IO;
using System.Text.Json;
using NAudio.Wave;

namespace CODMControllerProbe;

/// <summary>
/// 真人语音库：用户可自行添加提示音（MP3/WAV/M4A 复制到 AppData 语音目录），
/// 每个语音可勾选是否加入随机播放；内置两条自有录音（OK Let's go / Let's go）。
/// 配置存 %AppData%\CODMControllerProbe\voice-library.json，与 exe 位置无关。
/// </summary>
public class VoiceLibrary
{
    public List<VoiceClip> Clips { get; set; } = new();
    public float HumanVolume { get; set; } = 1.0f;   // 真人语音音量 0~1
    public List<string> TtsLines { get; set; } = new();
    public float TtsVolume { get; set; } = 1.0f;     // TTS 音量 0~1

    /// <summary>内置语音条目（嵌入资源，不占 AppData）。</summary>
    public static readonly (string name, string res)[] BuiltIns =
    {
        ("OK, Let's go（我的录音）", "assets.voice.voice_own_ok.wav"),
        ("Let's go（我的录音）", "assets.voice.voice_own_letsgo.wav"),
        ("录音样本 3（我的录音）", "assets.voice.voice_own_sample3.wav"),
    };
}

/// <summary>单条真人语音。</summary>
public class VoiceClip
{
    public string Name { get; set; } = "";
    /// <summary>相对文件名（AppData\voice\ 下）；内置条目为 "builtin:资源名"。</summary>
    public string File { get; set; } = "";
    /// <summary>是否加入随机播放。</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>语音库配置读写 + 播放（NAudio 控制真人音量，SAPI 控制 TTS 音量）。</summary>
public static class VoiceStore
{
    public static string Dir_ => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CODMControllerProbe", "voice");
    private static string JsonPath_ => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CODMControllerProbe", "voice-library.json");

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    /// <summary>当前语音库（进程内单例）。</summary>
    public static VoiceLibrary Lib { get; private set; } = Load();

    public static VoiceLibrary Load()
    {
        try
        {
            if (File.Exists(JsonPath_))
            {
                var lib = JsonSerializer.Deserialize<VoiceLibrary>(File.ReadAllText(JsonPath_));
                if (lib != null)
                {
                    // 保证内置条目始终存在（用户删除了也会恢复，防止随机库空）
                    EnsureBuiltIns(lib);
                    return lib;
                }
            }
        }
        catch { /* 损坏回退默认 */ }
        var fresh = new VoiceLibrary();
        EnsureBuiltIns(fresh);
        return fresh;
    }

    private static void EnsureBuiltIns(VoiceLibrary lib)
    {
        // 清理旧版本内置条目（资源已随版本移除，防止残留失效项）
        var validRes = VoiceLibrary.BuiltIns.Select(b => "builtin:" + b.res).ToHashSet();
        lib.Clips.RemoveAll(c => c.File.StartsWith("builtin:") && !validRes.Contains(c.File));

        foreach (var (name, res) in VoiceLibrary.BuiltIns)
        {
            if (!lib.Clips.Any(c => c.File == "builtin:" + res))
                lib.Clips.Add(new VoiceClip { Name = name, File = "builtin:" + res, Enabled = true });
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir_);
            File.WriteAllText(JsonPath_, JsonSerializer.Serialize(Lib, Opts));
        }
        catch { /* 保存失败不影响运行 */ }
    }

    /// <summary>添加真人语音：复制文件到 AppData\voice\ 并加入列表。</summary>
    public static string AddClip(string srcPath)
    {
        Directory.CreateDirectory(Dir_);
        string ext = Path.GetExtension(srcPath).ToLowerInvariant();
        string name = Path.GetFileNameWithoutExtension(srcPath);
        // 防重名：同名文件加序号
        string dest = Path.Combine(Dir_, Path.GetFileName(srcPath));
        int i = 1;
        while (File.Exists(dest))
        {
            dest = Path.Combine(Dir_, $"{name}_{i++}{ext}");
        }
        File.Copy(srcPath, dest);
        Lib.Clips.Insert(Math.Max(Lib.Clips.Count - VoiceLibrary.BuiltIns.Length, 0), // 内置条目放末尾，新增插在前面
            new VoiceClip { Name = Path.GetFileNameWithoutExtension(dest), File = Path.GetFileName(dest), Enabled = true });
        Save();
        return dest;
    }

    /// <summary>删除语音（内置条目标记禁用，不删除资源；用户文件删除磁盘文件）。</summary>
    public static void RemoveClip(VoiceClip clip)
    {
        if (clip.File.StartsWith("builtin:"))
        {
            clip.Enabled = false; // 内置：从随机池移除（保留可再启用）
        }
        else
        {
            try { File.Delete(Path.Combine(Dir_, clip.File)); } catch { }
            Lib.Clips.Remove(clip);
        }
        Save();
    }

    /// <summary>启用/禁用随机播放。</summary>
    public static void SetClipEnabled(VoiceClip clip, bool enabled)
    {
        clip.Enabled = enabled;
        Save();
    }
}

/// <summary>统一播放器：随机从启用池播放真人语音 / TTS，支持独立音量。</summary>
public static class VoicePlayer
{
    public const int Off = 0;
    public const int Tts = 1;
    public const int Human = 2;

    private static readonly Random Rand = new();
    private static object? _ttsVoice;
    private static readonly object PlayLock = new();

    // 当前 NAudio 播放对象（保持存活到播放结束）
    private static WaveOutEvent? _curOut;
    private static AudioFileReader? _curReader;
    private static string? _curTempFile;

    public static void Play(int mode)
    {
        if (mode == Tts) SpeakTts();
        else if (mode == Human) PlayRandomHuman();
    }

    public static void Preview(int mode)
    {
        if (mode == Tts) SpeakTts();
        else if (mode == Human) PlayRandomHuman();
    }

    // ---- 真人语音 ----

    private static void PlayRandomHuman()
    {
        var pool = VoiceStore.Lib.Clips.Where(c => c.Enabled).ToList();
        if (pool.Count == 0) return; // 全部禁用时静默
        var clip = pool[Rand.Next(pool.Count)];
        PlayClip(clip);
    }

    public static void PlayClip(VoiceClip clip)
    {
        try
        {
            string? path;
            if (clip.File.StartsWith("builtin:"))
            {
                // 内置资源：提取到临时文件播放（播放完删除）
                var asm = typeof(VoicePlayer).Assembly;
                using var rs = asm.GetManifestResourceStream(clip.File["builtin:".Length..]);
                if (rs == null) return;
                _curTempFile = Path.Combine(Path.GetTempPath(), "codmvoice_" + Guid.NewGuid().ToString("N") + ".wav");
                using (var fs = File.Create(_curTempFile)) rs.CopyTo(fs);
                path = _curTempFile;
            }
            else
            {
                path = Path.Combine(VoiceStore.Dir_, clip.File);
                if (!File.Exists(path)) return;
            }

            StopCurrent();
            _curReader = new AudioFileReader(path);
            _curOut = new WaveOutEvent { Volume = Math.Clamp(VoiceStore.Lib.HumanVolume, 0f, 1f) };
            _curOut.PlaybackStopped += (_, _) =>
            {
                _curOut?.Dispose(); _curOut = null;
                _curReader?.Dispose(); _curReader = null;
                if (_curTempFile != null) { try { File.Delete(_curTempFile); } catch { } _curTempFile = null; }
            };
            _curOut.Init(_curReader);
            _curOut.Play();
        }
        catch { /* 音频异常静默 */ }
    }

    private static void StopCurrent()
    {
        try { _curOut?.Stop(); } catch { }
    }

    // ---- TTS ----

    public static void SpeakTts()
    {
        var lines = VoiceStore.Lib.TtsLines;
        string word = lines.Count > 0
            ? lines[Rand.Next(lines.Count)]
            : (Rand.Next(2) == 0 ? "Locked in" : "Let's go"); // 无自定义句子时用默认词库
        SpeakLine(word);
    }

    public static void SpeakLine(string line)
    {
        try
        {
            lock (PlayLock)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                if (_ttsVoice == null)
                {
                    var t = Type.GetTypeFromProgID("SAPI.SpVoice");
                    if (t == null) return;
                    _ttsVoice = Activator.CreateInstance(t);
                    if (_ttsVoice == null) return;
                    try
                    {
                        dynamic voice = _ttsVoice;
                        dynamic en = voice.GetVoices("Language = 409");
                        if (en.Count > 0) voice.Voice = en.Item(0);
                    }
                    catch { /* 用默认语音 */ }
                }
                dynamic v = _ttsVoice;
                v.Volume = Math.Clamp((int)(VoiceStore.Lib.TtsVolume * 100f), 0, 100);
                v.Speak(line, 1); // 1 = SVSFlagsAsync：异步播报不阻塞
            }
        }
        catch { /* TTS 不可用时静默 */ }
    }
}
