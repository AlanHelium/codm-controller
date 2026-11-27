using System.Reflection;
using System.Windows.Forms;
using CODMControllerProbe;

/// <summary>
/// 输入方式切换的独立检查：枚举/文本、配置回读、误发防护，以及设置页第 7 区（实验）是否真的出现五种方式按钮。
/// 预览模式，不向任何游戏注入按键，不改写个人配置。
/// </summary>
static class InputModeChecks
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    static IEnumerable<Control> All(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in All(child)) yield return nested;
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        string folder = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/inputmode-check");
        Directory.CreateDirectory(folder);

        // ---- 枚举与文案 ----
        Check(InputChannel.Count == 5, "unexpected channel count");
        for (int i = 0; i < InputChannel.Count; i++)
            Check(InputChannel.Valid(i) && InputChannel.Name(i).Length > 0 && InputChannel.Hint(i).Length > 0, "channel " + i + " has no text");
        Check(!InputChannel.Valid(-1) && !InputChannel.Valid(5) && !InputChannel.Valid(42), "channel range check failed");

        // ---- 运行时切换 ----
        KeyInjection.Channel = 99; Check(KeyInjection.Channel == 0, "invalid channel was not clamped to 0");
        KeyInjection.Channel = 3; Check(KeyInjection.Channel == 3, "valid channel not stored");

        // ---- 配置回读 ----
        var defaults = AppSettings.CreateDefault();
        Check(defaults.InputChannel == 0, "default channel is not 0");
        defaults.InputChannel = 4;
        var round = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(defaults))!;
        Check(round.InputChannel == 4, "channel did not survive a config round-trip");
        string tmp = Path.Combine(folder, "probe-settings.json");
        Check(SettingsStore.SaveTo(tmp, round), "temporary config could not be written");
        var loaded = SettingsStore.LoadFrom(tmp)!;
        Check(loaded != null && loaded.InputChannel == 4, "channel was not read back from disk");
        File.Delete(tmp);

        // ---- 误发防护：没有窗口句柄、或目标就是本程序自己，一律不发 ----
        var probeKey = new GameKey(KeyboardKeys.E.Vk, KeyboardKeys.E.Scan);
        Check(!KeyInjection.Send(new GameKeyTarget(IntPtr.Zero, 9999u, "missing"), probeKey, true), "key was injected without a window handle");
        using var host = new Form { Text = "input mode check host", Width = 200, Height = 100 };
        host.Show();
        var self = new GameKeyTarget(host.Handle, (uint)Environment.ProcessId, "input mode check host");
        foreach (int channel in new[] { 0, 1, 2, 3, 4 })
        {
            KeyInjection.Channel = channel;
            Check(!KeyInjection.Send(self, probeKey, true), "channel " + channel + " injected into the app's own window");
        }
        host.Hide();
        KeyInjection.Channel = 0;

        // ---- 设置页实验区必须真的出现五种方式与当前选择 ----
        typeof(MainForm).Assembly.GetType("CODMControllerProbe.UiPreview")!
            .GetProperty("Enabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(null, true);
        using var main = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
        main.Show(); Application.DoEvents();
        typeof(MainForm).GetMethod("OpenSettings", Any)!.Invoke(main, new object?[] { 6 });
        Application.DoEvents();
        Form? settings = null;
        foreach (var control in All(main)) if (control.GetType().Name == "SettingsForm") settings = control as Form;
        Check(settings != null, "settings window did not open");
        var texts = All(settings!).Select(c => c.Text ?? string.Empty).ToList();
        var buttons = All(settings!).OfType<Button>().Select(b => b.Text).ToList();
        for (int i = 1; i <= InputChannel.Count; i++)
            Check(buttons.Any(t => t.Contains($"方式 {i}：")), $"method {i} button is missing on the experimental section");
        Check(texts.Any(t => t.Contains("当前：方式")), "current-method label is missing");
        foreach (var button in All(settings!).OfType<Button>().Where(b => b.Text.Contains("方式 ") && b.Parent != null))
            Check(button.Width <= button.Parent!.ClientSize.Width + 1, "method button overflows its card: " + button.Text + " width=" + button.Width + " parent=" + button.Parent!.ClientSize.Width);

        // ---- 点按钮必须真的切换：注入通道 + 主界面状态 + 落盘，再还原为默认 ----
        var previewEnabled = typeof(MainForm).Assembly.GetType("CODMControllerProbe.UiPreview")!
            .GetProperty("Enabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CODMControllerProbe", "settings.json");
        string configBackup = configPath + ".inputmode-check.bak";
        if (File.Exists(configPath)) File.Copy(configPath, configBackup, true);
        previewEnabled.SetValue(null, false);   // 预览模式会跳过保存；这里临时关闭以验证真实写入
        var method3 = All(settings!).OfType<Button>().First(b => b.Text.Contains("方式 3："));
        method3.PerformClick(); Application.DoEvents();
        Check(KeyInjection.Channel == 2, "clicking method 3 did not switch the injection channel");
        Check((int?)typeof(MainForm).GetField("_inputChannel", Any)!.GetValue(main) == 2, "main form did not record the new channel");
        Check(All(settings!).Any(c => (c.Text ?? string.Empty).Contains("当前：方式 3")), "current-method label did not refresh after clicking method 3");
        Check(File.Exists(configPath) && System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(configPath))!.InputChannel == 2, "method 3 was not persisted to settings.json");
        var method1 = method3.Parent!.Controls.OfType<Button>().First(b => b.Text.Contains("方式 1："));
        method1.PerformClick(); Application.DoEvents();
        Check(KeyInjection.Channel == 0, "restoring method 1 failed");
        Check((int?)typeof(MainForm).GetField("_inputChannel", Any)!.GetValue(main) == 0, "main form did not restore method 1");
        Check(System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(configPath))!.InputChannel == 0, "restoring method 1 was not persisted");
        if (File.Exists(configBackup)) { File.Copy(configBackup, configPath, true); File.Delete(configBackup); }
        previewEnabled.SetValue(null, true);
        Application.DoEvents();
        using var bitmap = new Bitmap(Math.Max(1, settings!.Width), Math.Max(1, settings.Height));
        settings.DrawToBitmap(bitmap, new Rectangle(Point.Empty, settings.Size));
        string shot = Path.Combine(folder, "设置-输入方式.png");
        bitmap.Save(shot);
        Console.WriteLine($"Input mode checks passed: {checks} assertions; screenshot: {shot}");
    }
}
