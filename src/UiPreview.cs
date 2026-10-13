namespace CODMControllerProbe;

internal static class UiPreview
{
    public static bool Enabled { get; set; }
    public static void Attach(Form form)
    {
        if(!Enabled) return;
        var timer=new System.Windows.Forms.Timer {Interval=750};
        timer.Tick+=(_,_)=>{
            if(form.IsDisposed) {timer.Dispose();return;}
            object Node(Control c) => new {
                Type=c.GetType().Name,c.Text,c.Visible,c.Bounds,c.PreferredSize,c.MinimumSize,c.MaximumSize,c.AutoSize,
                Dock=c.Dock.ToString(),Rows=c is TableLayoutPanel t?t.GetRowHeights():Array.Empty<int>(),
                Children=c.Controls.Cast<Control>().Where(x=>x.Visible).Select(Node).ToArray()
            };
            try {File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"layout-"+form.GetType().Name+".json"),
                System.Text.Json.JsonSerializer.Serialize(Node(form),new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));} catch{}
        };
        form.Shown+=(_,_)=>timer.Start(); form.FormClosed+=(_,_)=>timer.Dispose();
    }
}
