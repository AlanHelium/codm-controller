namespace CODMControllerProbe;
public partial class MainForm
{
    private Label? _editionSignature, _editionTag, _controllerSource;
    private string EditionTitle => Lang.T("CODM 手柄转换", "CODM Controller Bridge");
    private Action? BrandSettingsAction => null;
    private void InstallStandardPresentation()
    {
        RefreshEditionChrome();
        _controllerDiscovery.Changed += () => {
            if(IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(() => { if(!IsDisposed && !Disposing) RefreshStudioState(); }); }
            catch(InvalidOperationException) { }
        };
    }
    private void RefreshEditionChrome()
    {
        Text=EditionTitle; _lblTitle.Text=EditionTitle;
        _editionSignature!.Text="B U R R I T O";
        _editionTag!.Text=Lang.T("控制，从容一点","Stay in control");
    }
}
