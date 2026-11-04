using Burrito.Installer;
using CODMControllerProbe;

static class RepairChecks
{
    static async Task Main(string[] args)
    {
        int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        var bus=new ComponentState("ViGEmBus",true,true,true,false,"");
        var hid=new ComponentState("HidHide",true,true,true,false,"",false);
        var healthy=new ComponentReadiness(bus,hid,false);
        Check(RepairPolicy.Decide(healthy,false)==RepairAction.Healthy,"Healthy components must be reused");
        Check(RepairPolicy.Decide(healthy,true)==RepairAction.FinishCleanup,"A pending uninstall must not be overwritten by repair");
        Check(RepairPolicy.Decide(healthy with{SetupRestartPending=true},false)==RepairAction.Restart,"A real restart receipt must be reported");
        Check(RepairPolicy.Decide(healthy with{Vigem=bus with{RestartPending=true,RuntimeReady=false}},false)==RepairAction.Restart,"Pending removal must not trigger another install");
        Check(RepairPolicy.Decide(healthy with{HidHide=hid with{RuntimeReady=false}},false)==RepairAction.Repair,"Unavailable HidHide must be diagnosed");
        Check(RepairPolicy.Decide(healthy with{Vigem=bus with{Configured=false,RuntimeReady=false}},false)==RepairAction.Repair,"A leftover service without its device must require repair");
        Check(RepairPolicy.Decide(healthy with{Vigem=bus with{Installed=false,Configured=false,RuntimeReady=false}},false)==RepairAction.Repair,"A missing component must require repair");
        Check(RepairPolicy.Decide(healthy with{HidHide=hid with{Active=false}},false)==RepairAction.Healthy,"Inactive hiding is not a missing driver");
        Check(RepairPolicy.Decide(healthy with{SetupRestartPending=true},true)==RepairAction.Restart,"Restart must take precedence over unfinished cleanup");
        Check(Product.PayloadNames.Contains("修复连接.cmd"),"Installed package must include the repair entry");
        Check(Product.PayloadNames.Distinct(StringComparer.OrdinalIgnoreCase).Count()==Product.PayloadNames.Length,"Payload entries must be unique");
        Console.WriteLine($"PASS: {checks} repair policy and package checks; no driver or configuration writes.");
        if(args.Length==2&&args[0]=="--local-check")
        {
            var check=await ComponentRepair.Run(args[1],_=>{},true);
            Check(check.Success&&check.ReadyToLaunch&&!check.RestartRequired,"Local component check was not healthy: "+check.Summary);
            // The healthy return happens before the maintenance lock/installer.
            var reuse=await ComponentRepair.Run(args[1],_=>{});
            Check(reuse.Success&&reuse.ReadyToLaunch&&!reuse.RestartRequired,"Healthy reuse branch did not pass: "+reuse.Summary);
            Console.WriteLine("PASS: local check and healthy reuse; "+reuse.Summary+"; no component install or removal requested.");
        }
    }
}
