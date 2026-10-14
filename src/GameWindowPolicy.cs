namespace CODMControllerProbe;

public static class GameWindowPolicy
{
    private static readonly HashSet<string> OtherApps=new(new[]{"QQ","WeChat","Weixin","TIM","Codex","ChatGPT","Code","devenv","chrome","msedge","firefox","brave","opera","explorer"},StringComparer.OrdinalIgnoreCase);
    public static bool HasGameTitle(string title)=>title.Contains("使命召唤")||title.Contains("CODM",StringComparison.OrdinalIgnoreCase)||title.Contains("Call of Duty",StringComparison.OrdinalIgnoreCase);
    public static bool Matches(string processName,string title,IEnumerable<string> gameKeywords,bool explicitlySelected=false)
    {
        string name=processName.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?processName[..^4]:processName;
        if(StartupProcessPolicy.IsConverter(name))return false;
        // Chat windows, web pages and source editors can mention CODM without being the game.
        if(OtherApps.Contains(name))return false;
        if(explicitlySelected)return true;
        bool gameProcess=StartupProcessPolicy.BlocksConversion(name,gameKeywords);
        bool gameTitle=HasGameTitle(title);
        bool converterTitle=title.Contains("手柄转换")||title.Contains("ControllerProbe",StringComparison.OrdinalIgnoreCase)||title.Contains("Controller Bridge",StringComparison.OrdinalIgnoreCase);
        return gameProcess&&gameTitle&&!converterTitle;
    }
}
