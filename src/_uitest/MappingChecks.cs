using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using CODMControllerProbe;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

public class PadProxy : DispatchProxy
{
    public readonly HashSet<object> Buttons=new();public object? Dpad;
    protected override object? Invoke(MethodInfo? method,object?[]? args)
    {
        if(method!.Name=="SetButtonState"){if((bool)args![1]!)Buttons.Add(args[0]!);else Buttons.Remove(args[0]!);}
        if(method.Name=="SetDPadDirection")Dpad=args![0];
        if(method.Name=="ResetReport"){Buttons.Clear();Dpad=null;}
        return method.ReturnType==typeof(void)?null:method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;
    }
}
static class MappingChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks;static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Private)!.GetValue(o)!;
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,Private)!.SetValue(o,value);
    static object? Call(object o,string name,params object?[] args)=>o.GetType().GetMethod(name,Private)!.Invoke(o,args);
    static void Wait(Task task){long end=Environment.TickCount64+15000;while(!task.IsCompleted){if(Environment.TickCount64>end)throw new Exception("Async mapping did not complete");Application.DoEvents();Thread.Sleep(1);}task.GetAwaiter().GetResult();}
    static XInputNative.XInputGamepad Pad(params int[] sources){var g=new XInputNative.XInputGamepad();foreach(var s in sources){if(s==KeyMap.LT)g.bLeftTrigger=255;else if(s==KeyMap.RT)g.bRightTrigger=255;else g.wButtons|=(ushort)s;}return g;}
    static void Frame(MainForm main,params int[] sources)=>Call(main,"ProcessPhysicalFrame",Pad(sources),false);
    static IEnumerable<Control> Children(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in Children(c))yield return child;}}
    static void Render(Form form,string path){Application.DoEvents();using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(path);}
    [STAThread]static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.EnableVisualStyles();
        typeof(MainForm).Assembly.GetType("CODMControllerProbe.UiPreview")!.GetProperty("Enabled",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.SetValue(null,true);
        var gameKeywords=new[]{"codm","gameloop","androidemulator","aow_exe","appmarket"};
        foreach(string app in new[]{"QQ","WeChat","Weixin","TIM","Codex","ChatGPT","Code","devenv","chrome","msedge","firefox","brave","opera","explorer","notepad","CODMControllerProbe","CODMController-v3.4.6-Setup"})
            Check(!GameWindowPolicy.Matches(app,"群公告-CODM手柄玩家！！",gameKeywords),"A CODM title selected an unrelated process: "+app);
        Check(!GameWindowPolicy.Matches("QQ","CODM",gameKeywords,true),"Manual PID selection allowed a chat application");
        foreach(string game in new[]{"codm","aow_exe","AndroidEmulatorEn"})Check(GameWindowPolicy.Matches(game,"使命召唤手游 CODM",gameKeywords),"A real game process/title was rejected: "+game);
        Check(!GameWindowPolicy.Matches("AppMarket","腾讯手游助手首页",gameKeywords),"The launcher was selected as a game window");
        Check(GameWindowPolicy.Matches("CustomGame","Training",gameKeywords,true),"An explicitly selected game was rejected");
        string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);string Hash()=>File.Exists(SettingsStore.Path_)?Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(SettingsStore.Path_))):"missing";string before=Hash();
        var hiddenBefore=HidHideManager.TakeSnapshot();
        using var main=new MainForm{StartPosition=FormStartPosition.Manual,Location=new(-32000,-32000)};main.Show();Application.DoEvents();
        var proxy=DispatchProxy.Create<IDualShock4Controller,PadProxy>();var output=(PadProxy)(object)proxy;Set(main,"_ds4",proxy);
        var events=new List<(GameKey Key,bool Down)>();var destination=new GameKeyTarget((IntPtr)123456,9876,"Test game");
        var router=new GameKeyboardRouter(()=>destination,(_,key,down)=>{events.Add((key,down));return true;},_=>true);Set(main,"_gameKeys",router);
        bool accept=true;var decisions=new List<string[]>();Set(main,"_mappingDecision",(Func<string,string[],Task<bool>>)((_,lines)=>{decisions.Add(lines);return Task.FromResult(accept);}));
        foreach(string flag in new[]{"_expWasdMove","_expTouchMove","_gyroEnabled","_nudgeCursor","_enableFastProne","_enableNukeCombo","_enableViewHoldTab","_enableFastInteract","_enableRun","_fireModeEnabled"})Set(main,flag,false);
        Set(main,"_running",true);Set(main,"_physicalSessionReady",true);Set(main,"_loopStop",false);
        Check(!(bool)Call(main,"PreparePhysicalInputSession",new object?[]{null})!,"Preview tried to prepare a real input session");Call(main,"CheckNewPhysicalDevices");Call(main,"RestorePhysicalInputSession");Check(!Field<bool>(main,"_isolationApplied"),"Preview applied real device isolation");
        Call(main,"ReplaceKeyboardBindings",(object)Array.Empty<PadKeyBind>());Call(main,"RestoreActionBindings");Frame(main);
        var map=Field<KeyMap>(main,"_map");
        Frame(main,KeyMap.RT);Check(output.Buttons.SetEquals(new object[]{DualShock4Button.TriggerRight}),"Default RT does not only shoot");Frame(main);
        Call(main,"SetShoulderPreset",true);Frame(main);Frame(main,0x0200);Check(output.Buttons.SetEquals(new object[]{DualShock4Button.TriggerRight}),"Shoulder preset did not bind physical RB to shoot");Frame(main);
        Call(main,"AssignPhysicalAction",5,0x0100);Frame(main);Frame(main,0x0100);Check(output.Buttons.SetEquals(new object[]{DualShock4Button.TriggerRight}),"Rebinding while swapped used transformed rather than physical LB");Frame(main);
        Call(main,"SetShoulderPreset",false);Frame(main);Frame(main,KeyMap.LT);Check(output.Buttons.SetEquals(new object[]{DualShock4Button.TriggerRight}),"Disabling preset lost the custom shoot binding");Frame(main);
        for(int i=0;i<30;i++){Call(main,"SetShoulderPreset",true);Call(main,"SetShoulderPreset",false);}Frame(main,KeyMap.LT);Check(output.Buttons.SetEquals(new object[]{DualShock4Button.TriggerRight}),"Repeated preset toggles drifted the binding");Frame(main);
        foreach(int action in Enumerable.Range(0,9))foreach(int source in MappingPolicy.PadSources)
        {Call(main,"RestoreActionBindings");Frame(main);Call(main,"AssignPhysicalAction",action,source);Frame(main);Frame(main,source);Check(output.Buttons.Count==1,$"Action {action}, physical source {KeyMap.BitName(source)} emitted {output.Buttons.Count} native actions");Frame(main);Check(output.Buttons.Count==0,"Released action stayed held");}
        Call(main,"RestoreActionBindings");Frame(main);Call(main,"ReplaceKeyboardBindings",(object)new[]{PadKeyBind.Of(KeyMap.RT,KeyboardKeys.E)});Frame(main);events.Clear();Frame(main,KeyMap.RT);
        Check(output.Buttons.Count==0&&events.Any(e=>e.Down&&e.Key.Vk==KeyboardKeys.E.Vk),"Keyboard RT→E did not exclusively replace native RT");Frame(main);
        accept=false;Call(main,"AssignPhysicalAction",5,KeyMap.RT);Check(Field<List<PadKeyBind>>(main,"_padKeyMap").Any(b=>b.PadBit==KeyMap.RT),"Cancel changed the existing keyboard owner");
        accept=true;Call(main,"AssignPhysicalAction",5,KeyMap.RT);Frame(main);Frame(main,KeyMap.RT);Check(output.Buttons.SetEquals(new object[]{DualShock4Button.TriggerRight})&&!Field<List<PadKeyBind>>(main,"_padKeyMap").Any(),"Confirmed game action did not replace keyboard override");Frame(main);
        Set(main,"_fireModeEnabled",true);Set(main,"_fireModeUseCombo",false);Set(main,"_fireModeKey",KeyMap.RT);accept=false;
        Call(main,"SetShoulderPreset",true);Check(decisions.Last().Any(s=>s.Contains("射击模式长按 B"))&&Field<bool>(main,"_fireModeEnabled"),"Preset cancellation did not disclose or preserve the fire-mode owner");
        Call(main,"RestoreActionBindings");Check(decisions.Last().Any(s=>s.Contains("射击模式长按 B"))&&Field<bool>(main,"_fireModeEnabled"),"Defaults cancellation did not disclose or preserve the fire-mode owner");
        Call(main,"AssignPhysicalAction",5,0x0100);Check(decisions.Last().Any(s=>s.Contains("射击模式长按 B"))&&map.Shoot==KeyMap.RT,"Rebinding failed to disclose the former source's long-press owner");
        accept=true;Call(main,"RestoreActionBindings");Frame(main);Check(!Field<bool>(main,"_fireModeEnabled"),"Confirmed defaults did not clear the disclosed old owner");
        var chords=new[]{new PadKeyboardChord{PadBit=KeyMap.RT,KeyNames=new(){"V","B"}}};Wait((Task)Call(main,"SetKeyboardChords",true,chords)!);Frame(main);events.Clear();Frame(main,KeyMap.RT);
        Check(output.Buttons.Count==0&&events.Count(e=>e.Down)==2,"RT chord also emitted native shoot or did not press both keys");Frame(main);Check(events.Count(e=>!e.Down)==2,"Chord keys were not both released");
        Wait((Task)Call(main,"SetKeyboardChords",true,new[]{chords[0],new PadKeyboardChord{PadBit=0x0100,KeyNames=new(){"V","B"}}})!);Frame(main);events.Clear();Frame(main,KeyMap.RT,0x0100);Frame(main,0x0100);
        Check(!events.Any(e=>!e.Down),"Releasing one chord prematurely released shared keys");Frame(main);Check(events.Count(e=>!e.Down)==2,"Last chord release did not release shared keys");
        Call(main,"ReplaceKeyboardBindings",(object)new[]{PadKeyBind.Of(0x8000,KeyboardKeys.B)});Frame(main);events.Clear();Frame(main,KeyMap.RT,0x8000);Frame(main,0x8000);
        Check(events.Any(e=>!e.Down&&e.Key.Vk==KeyboardKeys.V.Vk)&&!events.Any(e=>!e.Down&&e.Key.Vk==KeyboardKeys.B.Vk),"Chord release cancelled a shared single-key mapping");
        object?[] pulse={KeyboardKeys.B.Vk,KeyboardKeys.B.Scan,"B",null};Call(main,"SendVirtualKey",pulse);Check(!events.Any(e=>!e.Down&&e.Key.Vk==KeyboardKeys.B.Vk),"A legacy pulse released another mapping's B");Frame(main);
        Frame(main,KeyMap.RT);Wait((Task)Call(main,"SetKeyboardChords",false,chords)!);events.Clear();Frame(main,KeyMap.RT);Check(output.Buttons.Count==0&&!events.Any(e=>e.Down),"Editing a held mapping immediately retriggered its new purpose");Frame(main);Frame(main,KeyMap.RT);Check(output.Buttons.SetEquals(new object[]{DualShock4Button.TriggerRight}),"Disabling chord did not restore native RT after release");Frame(main);
        Wait((Task)Call(main,"SetExperimentalCursor",true,KeyMap.RT)!);Frame(main);Frame(main,KeyMap.RT);Check(output.Buttons.Count==0&&Field<bool>(main,"_comboWasDown"),"Single cursor key also emitted original native action");Frame(main);Wait((Task)Call(main,"SetExperimentalCursor",false,KeyMap.RT)!);Frame(main,KeyMap.RT);Check(output.Buttons.SetEquals(new object[]{DualShock4Button.TriggerRight}),"Disabling cursor key did not restore native RT");Frame(main);
        var legacy=AppSettings.CreateDefault();legacy.ActionMappingVersion=0;legacy.SwapShoulderTriggers=true;legacy.ExpRsCombo=true;MappingPolicy.Migrate(legacy);Check(legacy.MapShoot==0x0200&&legacy.MapAim==0x0100&&legacy.EnableKeyboardChords&&legacy.KeyboardChords[0].PadBit==0x0080,"Legacy migration changed the effective source or lost R3 chord");MappingPolicy.Migrate(legacy);Check(legacy.MapShoot==0x0200&&legacy.KeyboardChords.Count==1,"Migration was not idempotent");
        Check(MappingPolicy.SinglePressed(Pad(KeyMap.RT,0x0100))==0,"Multi-button capture was accepted as a single source");
        foreach(bool swapped in new[]{false,true})foreach(int source in MappingPolicy.PadSources)foreach(int binding in MappingPolicy.PadSources){var raw=Pad(source);var old=ShoulderTriggerSwap.Apply(raw.wButtons,raw.bLeftTrigger,raw.bRightTrigger,swapped);Check(KeyMap.Hit(binding,old.Buttons,old.LeftTrigger,old.RightTrigger)==KeyMap.Hit(swapped?MappingPolicy.SwapSource(binding):binding,raw.wButtons,raw.bLeftTrigger,raw.bRightTrigger),"Migration did not preserve an effective physical source");}
        var config=(AppSettings)Call(main,"BuildSettings")!;var loaded=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(config))!;Check(loaded.ActionMappingVersion==2&&loaded.KeyboardChords.Count==config.KeyboardChords.Count,"Mapping config did not round-trip");
        Call(main,"SetMappingCapture",true);Frame(main,KeyMap.RT);Check(output.Buttons.Count==0,"Recording forwarded an original game action");Check(((XInputNative.XInputGamepad?)Call(main,"ReadPhysicalCapture"))!.Value.bRightTrigger==255,"Recording did not see raw physical trigger");Call(main,"SetMappingCapture",false);Frame(main,KeyMap.RT);Check(output.Buttons.Count==0,"Recording completion retriggered a held trigger");Frame(main);
        Call(main,"ReplaceKeyboardBindings",(object)new[]{PadKeyBind.Of(KeyMap.RT,KeyboardKeys.E)});Set(main,"_mappingDecision",null!);Call(main,"OpenRemap",false);Application.DoEvents();Call(main,"SetShoulderPreset",true);Application.DoEvents();
        Check(Children(main).OfType<Form>().Any(f=>f.Text=="绑定冲突"),"Conflict did not appear inside main window");Render(main,Path.Combine(folder,"映射冲突-主界面内.png"));Children(main).OfType<Button>().Single(b=>b.Text=="取消，保留旧设置").PerformClick();Application.DoEvents();Check(!Field<bool>(main,"_swapShoulderTriggers"),"Inline cancellation changed preset");
        Set(main,"_mappingDecision",(Func<string,string[],Task<bool>>)((_,_)=>Task.FromResult(true)));Call(main,"ShowStudioSection",7);Application.DoEvents();Render(main,Path.Combine(folder,"实验功能.png"));
        Check(Children(main).OfType<Button>().Any(b=>b.Text=="配置实验组合映射 →"),"Experimental chord editor is missing");
        Set(main,"_mappingDecision",null!);var settings=Children(main).OfType<SettingsForm>().Single();var cursor=Children(main).OfType<CheckBox>().Single(c=>c.Text=="启用单键切光标");cursor.Checked=true;Application.DoEvents();
        Check(Children(main).OfType<Form>().Any(f=>f.Text=="绑定冲突"),"Single cursor conflict was not disclosed");Children(main).OfType<Button>().Single(b=>b.Text=="替换 / 交换并应用").PerformClick();
        long deadline=Environment.TickCount64+3000;while(!Field<bool>(main,"_experimentalCursorEnabled")&&Environment.TickCount64<deadline){Application.DoEvents();Thread.Sleep(1);}Application.DoEvents();
        Check(Field<bool>(main,"_experimentalCursorEnabled")&&!settings.IsDisposed&&cursor.Checked,"Root-setting conflict disposed the caller or failed to apply cursor binding");cursor.Checked=false;Application.DoEvents();
        Check(!Field<bool>(main,"_experimentalCursorEnabled"),"Disabling the cursor did not apply");
        Set(main,"_mappingDecision",(Func<string,string[],Task<bool>>)((_,_)=>Task.FromResult(true)));
        Children(main).OfType<Button>().Single(b=>b.Text=="配置实验组合映射 →").PerformClick();Application.DoEvents();Render(main,Path.Combine(folder,"键盘组合编辑.png"));
        Set(main,"_running",false);main.Close();Check(before==Hash(),"Checks modified personal settings");Check(decisions.Any(d=>d.Any(line=>line.Contains("E"))),"Keyboard conflict did not disclose previous E binding");
        var hiddenAfter=HidHideManager.TakeSnapshot();
        if(hiddenBefore.Success||hiddenAfter.Success)Check(hiddenBefore.Success&&hiddenAfter.Success&&hiddenBefore.Active==hiddenAfter.Active&&hiddenBefore.Inverse==hiddenAfter.Inverse&&hiddenBefore.Blacklist.Order().SequenceEqual(hiddenAfter.Blacklist.Order())&&hiddenBefore.Whitelist.Order().SequenceEqual(hiddenAfter.Whitelist.Order()),"Preview changed actual isolation settings");
        File.WriteAllText(Path.Combine(folder,"映射回归.json"),JsonSerializer.Serialize(new{Passed=true,Assertions=checks,MockOutput=true,NoActualKeyInjection=true,NoDriverChanges=hiddenBefore.Success?(bool?)true:null,PreviewIsolationGuard=true,SettingsUnchanged=true},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Mapping checks passed: {checks} assertions; mock input; isolation snapshot available: {hiddenBefore.Success}");
    }
}
