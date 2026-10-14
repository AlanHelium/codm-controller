namespace CODMControllerProbe;
public sealed class KeyboardChordForm : Form
{
    private readonly UiStack _body;
    private readonly List<(ComboBox Pad,ComboBox First,ComboBox Second)> _rows=new();
    private readonly Func<bool,List<PadKeyboardChord>,Task<bool>> _commit;
    private readonly CheckBox _enabled;
    private readonly Label _hint;
    public KeyboardChordForm(bool enabled,IEnumerable<PadKeyboardChord> bindings,Func<bool,List<PadKeyboardChord>,Task<bool>> commit)
    {
        _commit=commit;Text=Lang.T("单个手柄键 → 两个键盘键","One pad button → two keyboard keys");Assets.Skin(this);Font=Assets.Ui(10);
        var page=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(Dpi.Px(12))};_body=UiLayout.Stack(16);page.Controls.Add(_body);Controls.Add(page);
        _enabled=UiLayout.Check(_body,Lang.T("启用实验键盘组合映射","Enable experimental keyboard chords"),enabled,_=>{});
        UiLayout.Add(_body,UiLayout.Text(Lang.T("按住手柄键时按下两个键盘键，松开时一起释放。原手柄动作被替代；冲突会在应用前列出。","Hold the pad button to press both keyboard keys; release it to release both. Its original pad action is replaced. Conflicts are shown before applying.")));
        _hint=UiLayout.Text("");UiLayout.Add(_body,_hint);foreach(var item in bindings)AddRow(item);
        UiLayout.Action(_body,Lang.T("添加组合映射","Add chord binding"),()=>{AddRow(new());UiLayout.FitText(this);});
        UiLayout.Action(_body,Lang.T("应用设置","Apply settings"),async()=>{
            var values=_rows.Select(r=>new PadKeyboardChord{PadBit=MappingPolicy.PadSources[r.Pad.SelectedIndex],KeyNames=new(){KeyboardKeys.All[r.First.SelectedIndex].Name,KeyboardKeys.All[r.Second.SelectedIndex].Name}}).ToList();
            if(values.Any(v=>v.Normalize()==null)){_hint.Text=Lang.T("请选择两个不同的键盘键。","Choose two different keyboard keys.");return;}
            if(values.GroupBy(v=>v.PadBit).Any(g=>g.Count()>1)){_hint.Text=Lang.T("同一个手柄键只能有一条组合映射，请移除重复项。","Each pad button can have only one chord; remove duplicates.");return;}
            if(await _commit(_enabled.Checked,values))Close();
        },true);UiLayout.Action(_body,Lang.T("取消","Cancel"),()=>Close());
    }
    internal static ComboBox PadPicker(int source)
    {var c=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Font=Assets.Ui(10),Margin=new Padding(0,0,0,Dpi.Px(10))};c.Items.AddRange(MappingPolicy.PadSources.Select(KeyMap.BitName).Cast<object>().ToArray());c.SelectedIndex=Math.Max(0,Array.IndexOf(MappingPolicy.PadSources,source));return c;}
    internal static ComboBox KeyPicker(string name)
    {var c=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Font=Assets.Ui(10),Margin=new Padding(0,0,0,Dpi.Px(10))};c.Items.AddRange(KeyboardKeys.All.Select(k=>k.Name).Cast<object>().ToArray());c.SelectedIndex=Math.Max(0,KeyboardKeys.All.ToList().FindIndex(k=>k.Name==name));return c;}
    private void AddRow(PadKeyboardChord item)
    {
        var card=UiLayout.Stack(16);card.IsCard=true;card.BackColor=StudioTheme.Surface;card.Margin=new Padding(0,0,0,Dpi.Px(12));
        UiLayout.Add(card,UiLayout.Text(Lang.T("实体手柄键","Physical pad button"),true));var pad=PadPicker(item.PadBit);UiLayout.Add(card,pad);
        UiLayout.Add(card,UiLayout.Text(Lang.T("键盘组合（两个键同时按住）","Keyboard chord (hold both keys)")));
        var first=KeyPicker(item.KeyNames[0]);var second=KeyPicker(item.KeyNames[1]);UiLayout.Add(card,first);UiLayout.Add(card,second);_rows.Add((pad,first,second));
        UiLayout.Action(card,Lang.T("移除此映射","Remove binding"),()=>{_rows.Remove((pad,first,second));_body.Controls.Remove(card);card.Dispose();UiLayout.FitText(this);});
        _body.Controls.Add(card);_body.Controls.SetChildIndex(card,Math.Min(_body.Controls.Count-1,3+_rows.Count-1));
    }
}
