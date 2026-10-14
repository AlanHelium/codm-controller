namespace CODMControllerProbe;

public static class MappingPolicy
{
    public static string ActionName(int index)=>index switch{0=>Lang.T("跳跃","Jump"),1=>Lang.T("下蹲","Crouch"),2=>Lang.T("换弹","Reload"),3=>Lang.T("切枪","Switch weapon"),4=>Lang.T("瞄准","Aim"),5=>Lang.T("射击","Shoot"),6=>Lang.T("战术道具","Tactical"),7=>Lang.T("杀伤道具","Lethal"),8=>Lang.T("近战/标点","Melee / ping"),_=>"?"};
    public static readonly int[] PadSources={0x1000,0x2000,0x4000,0x8000,0x0100,0x0200,KeyMap.LT,KeyMap.RT,0x0040,0x0080,1,2,4,8,0x0010,0x0020,0x0400};
    public static int SwapSource(int source)=>source switch{0x0100=>KeyMap.LT,0x0200=>KeyMap.RT,KeyMap.LT=>0x0100,KeyMap.RT=>0x0200,_=>source};
    public static int Pressed(XInputNative.XInputGamepad g)=>g.wButtons|(g.bLeftTrigger>10?KeyMap.LT:0)|(g.bRightTrigger>10?KeyMap.RT:0);
    public static int SinglePressed(XInputNative.XInputGamepad g)
    {int bits=Pressed(g);return bits!=0&&(bits&(bits-1))==0&&SettingsStore.IsValidBind(bits)?bits:0;}
    public static void SwapActions(KeyMap map)
    {
        int[] b=Enumerable.Range(0,9).Select(i=>SwapSource(map.ActionBinding(i))).ToArray();
        map.Jump=b[0];map.Crouch=b[1];map.Reload=b[2];map.Switch=b[3];map.Aim=b[4];map.Shoot=b[5];map.Tactical=b[6];map.Lethal=b[7];map.MeleePing=b[8];
    }
    public static void Migrate(AppSettings s)
    {
        s.KeyboardChords??=new();
        if(s.ActionMappingVersion>=2)return;
        if(s.SwapShoulderTriggers){s.MapJump=SwapSource(s.MapJump);s.MapCrouch=SwapSource(s.MapCrouch);s.MapReload=SwapSource(s.MapReload);s.MapSwitch=SwapSource(s.MapSwitch);s.MapAim=SwapSource(s.MapAim);s.MapShoot=SwapSource(s.MapShoot);s.MapTactical=SwapSource(s.MapTactical);s.MapLethal=SwapSource(s.MapLethal);s.MeleeKey=SwapSource(s.MeleeKey);s.InteractKey=SwapSource(s.InteractKey);}
        if(s.ExpRsCombo){s.KeyboardChords.RemoveAll(c=>c==null||c.PadBit==0x0080);s.KeyboardChords.Add(new(){PadBit=0x0080,KeyNames=new(){"V","B"}});s.EnableKeyboardChords=true;s.ExpRsCombo=false;}
        s.ActionMappingVersion=2;
    }
}

public sealed class PadKeyboardChord
{
    public int PadBit {get;set;}=KeyMap.RT;
    public List<string> KeyNames {get;set;}=new(){"V","B"};
    public PadKeyboardChord? Normalize()
    {
        var keys=(KeyNames??new()).Select(KeyboardKeys.ByName).ToArray();
        if(!SettingsStore.IsValidBind(PadBit)||keys.Length!=2||keys.Any(k=>k==null)||keys[0]==keys[1])return null;
        return new(){PadBit=PadBit,KeyNames=keys.Select(k=>k!.Name).ToList()};
    }
    public string Name=>string.Join(" + ",KeyNames);
}
