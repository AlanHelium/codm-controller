using System.Reflection;
using CODMControllerProbe;
using System.Security.Cryptography;
using System.Text.Json;
static class FreeUiChecks
{
    [STAThread]static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
        typeof(MainForm).Assembly.GetType("CODMControllerProbe.UiPreview")!.GetProperty("Enabled",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.SetValue(null,true);
        string folder=args[0];Directory.CreateDirectory(folder);string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CODMControllerProbe","settings.json");
        string Hash()=>File.Exists(settings)?Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(settings))):"missing";string before=Hash();
        using var main=new MainForm();var field=typeof(MainForm).GetField("_versionLevel",BindingFlags.Instance|BindingFlags.NonPublic)!;
        if(!field.IsInitOnly||(int)field.GetValue(main)!!=2||AppConfig.FreeDemo)throw new Exception("Feature access still depends on legacy tier");
        if(typeof(MainForm).Assembly.GetType("CODMControllerProbe.License")!=null)throw new Exception("Legacy activation implementation remains");
        using var voice=new VoiceSettingsForm(VoicePlayer.Off,false,_=>{});
        if(!(bool)typeof(VoiceSettingsForm).GetField("_isPro",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(voice)!)throw new Exception("Legacy false flag still disables voice library");
        main.StartPosition=FormStartPosition.Manual;main.Location=new(-32000,-32000);main.Show();for(int i=0;i<4;i++)Application.DoEvents();
        using(var shot=new Bitmap(main.Width,main.Height)){main.DrawToBitmap(shot,new Rectangle(Point.Empty,main.Size));shot.Save(Path.Combine(folder,"免费版-首页.png"));}
        typeof(MainForm).GetMethod("OpenUnlock",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,null);for(int i=0;i<4;i++)Application.DoEvents();
        using(var shot=new Bitmap(main.Width,main.Height)){main.DrawToBitmap(shot,new Rectangle(Point.Empty,main.Size));shot.Save(Path.Combine(folder,"免费版-支持作者.png"));}
        if(Hash()!=before)throw new Exception("UI preview changed actual settings");
        File.WriteAllText(Path.Combine(folder,"免费版-验证.json"),JsonSerializer.Serialize(new{Passed=true,AllFeaturesOpen=true,LegacyActivationAbsent=true,VoiceOpenWithLegacyFalse=true,SettingsUnchanged=true},new JsonSerializerOptions{WriteIndented=true}));
        main.Close();Console.WriteLine("Free UI checks passed; actual settings unchanged");
    }
}
