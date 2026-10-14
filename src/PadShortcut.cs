namespace CODMControllerProbe;

public sealed record PadShortcut(bool Enabled,bool UseCombo,int Source,int Combo,string Target,int HoldMs)
{
    public int Mask=>UseCombo?Combo:Source;
    public bool Valid=>KeyboardKeys.ByName(Target)!=null&&(UseCombo?MappingPolicy.PadSources.Count(s=>(Combo&s)!=0)==2&&(Combo&~MappingPolicy.PadSources.Aggregate(0,(a,s)=>a|s))==0:SettingsStore.IsValidBind(Source));
    public IEnumerable<int> Sources=>MappingPolicy.PadSources.Where(s=>(Mask&s)!=0);
    public string Trigger=>string.Join(" + ",Sources.Select(KeyMap.BitName));
    public string Description=>Trigger+" → "+Target+" · "+(HoldMs==0?Lang.T("立即","Immediate"):HoldMs+" ms");
}
