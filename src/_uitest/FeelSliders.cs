using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using CODMControllerProbe;

static class FeelSliderChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Private)!.GetValue(target)!;
    static void Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private)!.Invoke(target,args);
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
    static void Near(float actual,float expected,string message)=>Check(Math.Abs(actual-expected)<.0001f,message+$" ({actual} != {expected})");
    static IEnumerable<Control> Children(Control root){foreach(Control child in root.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}}
    static void Key(Control slider,Keys key)=>slider.GetType().GetMethod("OnKeyDown",Private)!.Invoke(slider,[new KeyEventArgs(key)]);
    static void Value(Control slider,int value)=>slider.GetType().GetProperty("Value")!.SetValue(slider,value);
    static AppSettings Settings(MainForm main)=>(AppSettings)typeof(MainForm).GetMethod("BuildSettings",Private)!.Invoke(main,null)!;
    static FeelForm Open(MainForm main){Call(main,"OpenFeelForm");Application.DoEvents();return Children(main).OfType<FeelForm>().Single();}

    [STAThread]static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
        typeof(MainForm).Assembly.GetType("CODMControllerProbe.UiPreview")!.GetProperty("Enabled",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.SetValue(null,true);
        string folder=args[0];Directory.CreateDirectory(folder);
        string actualSettings=SettingsStore.Path_;
        string Hash()=>File.Exists(actualSettings)?Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(actualSettings))):"missing";
        string before=Hash();
        using var main=new MainForm();main.StartPosition=FormStartPosition.Manual;main.Location=new(-32000,-32000);main.Show();Application.DoEvents();
        var feel=Open(main);
        var masterNames=new[]{"_masterLeft","_masterRight","_masterRightAds"};
        var outputNames=new[]{"_curveLeft","_curveRight","_curveRightAds"};
        for(int i=0;i<3;i++)
        {
            Call(feel,"SwitchStick",i);
            var anti=Field<Control>(feel,"trkAnti");Key(anti,Keys.End);
            Near(Field<FeelCurve>(main,masterNames[i]).Anti,.50f,$"Stick {i}: anti-deadzone slider moved without applying");
            Check(!Field<bool>(feel,"_loadingParameters"),"Parameter loading guard stayed enabled");
            int value=31+i*3;Value(anti,value);
            Near(Field<FeelCurve>(main,masterNames[i]).Anti,value/100f,"Master anti-deadzone did not update");
            Near(Field<FeelCurve>(main,outputNames[i]).Eval(0),value/100f,"Live output curve did not update");
            Check(Field<Label>(feel,"lblAntiVal").Text==value+"%","Anti-deadzone label is stale");
            if(i>0)
            {
                var x=Field<Control>(feel,"trkGainX");var y=Field<Control>(feel,"trkGainY");
                Key(x,Keys.Home);Near(Field<FeelCurve>(main,outputNames[i]).GainX,.5f,"Horizontal keyboard minimum did not apply");
                Key(x,Keys.End);Near(Field<FeelCurve>(main,outputNames[i]).GainX,2f,"Horizontal keyboard maximum did not apply");
                Value(x,75);Value(y,65);
                Near(Field<FeelCurve>(main,outputNames[i]).GainX,1.25f,"Horizontal slider did not apply");
                Near(Field<FeelCurve>(main,outputNames[i]).GainY,1.15f,"Vertical slider did not apply");
                Near(Settings(main).FineTuneGains[i*2],1.25f,"Horizontal gain did not reach saved settings");
                Near(Settings(main).FineTuneGains[i*2+1],1.15f,"Vertical gain did not reach saved settings");
            }
        }
        // Exercise pointer capture through the real slider event handlers, inside this offscreen test form.
        var mouseSlider=Field<Control>(feel,"trkAnti");
        mouseSlider.GetType().GetMethod("OnMouseDown",Private)!.Invoke(mouseSlider,[new MouseEventArgs(MouseButtons.Left,1,10,mouseSlider.Height/2,0)]);
        mouseSlider.GetType().GetMethod("OnMouseMove",Private)!.Invoke(mouseSlider,[new MouseEventArgs(MouseButtons.Left,0,mouseSlider.Width-1,mouseSlider.Height/2,0)]);
        mouseSlider.GetType().GetMethod("OnMouseUp",Private)!.Invoke(mouseSlider,[new MouseEventArgs(MouseButtons.Left,1,mouseSlider.Width-1,mouseSlider.Height/2,0)]);
        Near(Field<FeelCurve>(main,"_masterRightAds").Anti,.5f,"Pointer drag did not reach live curve");
        Value(mouseSlider,37);
        var rate=Children(feel).Single(c=>c.GetType().Name=="StudioSlider"&&(int)c.GetType().GetProperty("Maximum")!.GetValue(c)! == 1000);
        Key(rate,Keys.Home);Check(Settings(main).OutputHz==60,"Output frequency minimum did not apply");
        Value(rate,500);Check(Settings(main).OutputHz==500,"Output frequency slider did not apply");
        feel.Close();Application.DoEvents();
        Call(main,"ShowStudioSection",1);Application.DoEvents();
        Key(Field<Control>(main,"_studioSensitivity"),Keys.End);Check(Settings(main).FreeSensLevel==19,"Main sensitivity slider did not apply");
        Value(Field<Control>(main,"_studioAdsSlider"),125);Near(Settings(main).AdsMultiplier,1.25f,"Main ADS slider did not apply");
        feel=Open(main);Call(feel,"SwitchStick",1);Value(Field<Control>(feel,"trkAnti"),39);
        Near(Field<FeelCurve>(main,"_curveRight").Eval(0),.39f,"Reopened editor stayed blocked");
        var saved=Settings(main);
        string export=Path.Combine(folder,"preview-settings.json");
        Check(SettingsStore.SaveTo(export,saved),"Could not export settings to test directory");
        var reloaded=JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(export))!;
        Near(reloaded.Left.ToCurve().Anti,.31f,"Left anti-deadzone was not persisted");
        Near(reloaded.Right.ToCurve().Anti,.39f,"Right anti-deadzone was not persisted");
        Near(reloaded.RightAds.ToCurve().Anti,.37f,"ADS anti-deadzone was not persisted");
        Near(reloaded.Packs.Single(p=>p.Key==reloaded.ActivePack).Right.ToCurve().Anti,.39f,"Active curve preset was not persisted");
        Near(reloaded.FineTuneGains[2],1.25f,"Gain was not persisted");
        Check(reloaded.FreeSensLevel==19&&reloaded.OutputHz==500,"Main sliders were not persisted");
        Check(before==Hash(),"Offscreen checks changed personal configuration");
        using(var bitmap=new Bitmap(main.Width,main.Height)){main.DrawToBitmap(bitmap,new Rectangle(Point.Empty,main.Size));bitmap.Save(Path.Combine(folder,"滑块修复-曲线编辑.png"));}
        main.Close();
        File.WriteAllText(Path.Combine(folder,"滑块回归检查.json"),JsonSerializer.Serialize(new{Passed=true,Assertions=checks,PointerAndKeyboard=true,AllThreeSticks=true,LiveCurvesAndSettings=true,ActualUserSettingsUnchanged=true},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Feel slider checks passed: {checks} assertions; actual user settings unchanged");
    }
}
