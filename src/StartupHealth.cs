namespace CODMControllerProbe;

public static class StartupProcessPolicy
{
    public static bool IsConverter(string name)=>name.Contains("ControllerProbe",StringComparison.OrdinalIgnoreCase)||name.StartsWith("CODMController",StringComparison.OrdinalIgnoreCase)||name.Contains("手柄转换");
    public static bool BlocksConversion(string name,IEnumerable<string> gameKeywords)=>!IsConverter(name)&&gameKeywords.Any(k=>name.Contains(k,StringComparison.OrdinalIgnoreCase));
}

public enum HealthLevel {Ready,Waiting,Info,Error}
public sealed record HealthRow(string Name,HealthLevel Level,string Summary,string Detail="");
public sealed record StartupDiagnosticData(ComponentReadiness Components,bool PhysicalConnected,string ControllerName,bool Running,bool Preparing,bool InputReadable,bool VirtualConnected,bool IsolationReady,string ConversionError,bool AudioRequested,bool AudioRunning)
{
    public HealthRow[] Rows()
    {
        HealthRow Driver(ComponentState state,string name)
        {
            if(state.RestartPending)return new(name,HealthLevel.Waiting,Lang.T("已安装 · 等待重启完成","Installed · restart pending"));
            if(!state.Installed)return new(name,HealthLevel.Error,Lang.T("未安装","Not installed"),Lang.T("请运行安装包或准备组件工具。","Run Setup or Prepare components."));
            if(!state.Configured)return new(name,HealthLevel.Error,Lang.T("安装配置不完整","Installation configuration is incomplete"),Lang.T("请使用组件准备工具修复。","Repair with Prepare components."));
            if(!state.RuntimeReady)return new(name,HealthLevel.Waiting,Lang.T("已安装 · 接口尚未就绪","Installed · interface not ready"),state.Error);
            return new(name,HealthLevel.Ready,Lang.T("已安装 · 接口可用","Installed · interface available"));
        }
        var hid=Driver(Components.HidHide,Lang.T("HidHide 实体输入隔离组件","HidHide isolation component"));
        if(hid.Level==HealthLevel.Ready)hid=hid with {Detail=IsolationReady?Lang.T("当前转换会话隔离检查通过。","Current conversion session isolation verified."):Lang.T("启动转换时会启用并验证隔离；未启用隐藏不代表未安装。","Conversion enables and verifies isolation; inactive hiding does not mean the driver is missing.")};
        var pad=!PhysicalConnected?new HealthRow(Lang.T("实体手柄","Physical controller"),HealthLevel.Waiting,Lang.T("等待连接手柄","Waiting for a controller")):
            new HealthRow(Lang.T("实体手柄","Physical controller"),HealthLevel.Ready,ControllerName,InputReadable?Lang.T("正在读取输入。","Input is being read."):Lang.T("设备已识别，输入将在转换启动后读取。","Device identified; input reading starts with conversion."));
        var output=!Components.Ready?new HealthRow(Lang.T("转换状态","Conversion status"),HealthLevel.Waiting,Lang.T("等待组件就绪","Waiting for components"),Components.Message):
            ConversionError.Length>0?new HealthRow(Lang.T("转换状态","Conversion status"),HealthLevel.Error,ConversionError):
            new HealthRow(Lang.T("转换状态","Conversion status"),Preparing?HealthLevel.Waiting:Running&&VirtualConnected&&IsolationReady?HealthLevel.Ready:HealthLevel.Info,
                Preparing?Lang.T("正在准备转换","Preparing conversion"):Running&&VirtualConnected&&IsolationReady?Lang.T("转换输出已启动","Conversion output started"):Lang.T("尚未启动转换","Conversion has not started"));
        var audio=new HealthRow(Lang.T("系统音频采集 · 枪声震动","System audio · shot rumble"),!AudioRequested?HealthLevel.Info:AudioRunning?HealthLevel.Ready:HealthLevel.Waiting,
            !AudioRequested?Lang.T("当前无需采集","Not requested"):AudioRunning?Lang.T("正在采集","Capturing"):Lang.T("音频采集尚未就绪","Audio capture not ready"),Lang.T("只影响枪声震动，不影响手柄转换。","Only affects shot rumble, not controller conversion."));
        return [Driver(Components.Vigem,Lang.T("ViGEmBus 虚拟手柄驱动","ViGEmBus virtual pad driver")),hid,pad,output,audio];
    }
}
