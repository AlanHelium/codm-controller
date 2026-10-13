using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CODMControllerProbe;

internal sealed class StudioControllerHero : Control
{
    private readonly Dictionary<string,Image> _images=new();
    private static readonly Bitmap GlowTexture=CreateGlowTexture();
    private readonly System.Windows.Forms.Timer _animation=new(){Interval=33};
    private string _model="xbox-generic",_previous="";
    private float _brightness,_from,_target;
    private long _transitionTick=Environment.TickCount64,_modelTick;
    private bool _connected;
    public StudioControllerHero()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);
        BackColor=StudioTheme.Background;AccessibleName=Lang.T("手柄连接状态图","Controller connection illustration");
        _animation.Tick+=(_,_)=>{UpdateBrightness();Invalidate();UpdateAnimation();};
    }
    internal void SetController(ControllerVisual visual)
    {
        if(visual.Asset!=_model){_previous=_model;_model=visual.Asset;_modelTick=Environment.TickCount64;}
        if(_connected!=visual.Connected){UpdateBrightness();_from=_brightness;_connected=visual.Connected;_target=_connected?1:0;_transitionTick=Environment.TickCount64;}
        AccessibleDescription=visual.Title+(_connected?Lang.T("，已连接","; connected"):Lang.T("，未连接","; disconnected"));
        UpdateAnimation();Invalidate();
    }
    private void UpdateBrightness()
    {
        float progress=Math.Clamp((Environment.TickCount64-_transitionTick)/(_target>_from?450f:250f),0,1);
        float ease=1-MathF.Pow(1-progress,3);_brightness=_from+(_target-_from)*ease;
    }
    private void UpdateAnimation()
    {
        bool visible=Visible&&FindForm()?.WindowState!=FormWindowState.Minimized;
        bool animate=visible&&(_connected||Math.Abs(_brightness-_target)>.002f||Environment.TickCount64-_modelTick<220);
        if(animate)_animation.Start();else _animation.Stop();
    }
    protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);UpdateBrightness();UpdateAnimation();}
    protected override void OnPaintBackground(PaintEventArgs e)
    {using var background=new SolidBrush(BackColor);e.Graphics.FillRectangle(background,ClientRectangle);}
    private Image GetImage(string model)
    {
        if(!_images.TryGetValue(model,out var image))_images[model]=image=Assets.LoadImage("assets.controllers."+model+".png");
        return image;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;
        UpdateBrightness();float breath=_connected?.94f+.06f*MathF.Sin(Environment.TickCount64/3400f*MathF.PI*2):1;
        float light=_brightness*breath;
        float width=Math.Min(Width*.95f,Height*1.48f),height=width*2/3;
        var imageRect=new RectangleF((Width-width)/2,(Height-height)/2,width,height);
        Glow(g,new RectangleF(Width*.015f,-Height*.03f,Width*.97f,Height*1.00f),Color.FromArgb((int)(125*light),255,184,91));
        Glow(g,new RectangleF(imageRect.X+imageRect.Width*.14f,imageRect.Bottom-imageRect.Height*.08f,imageRect.Width*.72f,imageRect.Height*.13f),Color.FromArgb(85,0,0,0));
        float switchProgress=Math.Clamp((Environment.TickCount64-_modelTick)/220f,0,1);
        if(_previous.Length>0&&switchProgress<1)DrawController(g,GetImage(_previous),imageRect,(1-switchProgress)*(.68f+.32f*light));
        DrawController(g,GetImage(_model),imageRect,(_previous.Length>0?switchProgress:1)*(.68f+.32f*light));
        if(light>.003f)DrawLights(g,imageRect,light*(_previous.Length>0?switchProgress:1));
    }
    private static void DrawController(Graphics g,Image image,RectangleF rect,float opacity)
    {
        using var attrs=new ImageAttributes();var matrix=new ColorMatrix{Matrix33=opacity};attrs.SetColorMatrix(matrix);
        g.DrawImage(image,Rectangle.Round(rect),0,0,image.Width,image.Height,GraphicsUnit.Pixel,attrs);
    }
    private static void Glow(Graphics g,RectangleF rect,Color color)
    {
        if(rect.Width<=0||rect.Height<=0||color.A==0)return;
        using var attrs=new ImageAttributes();var matrix=new ColorMatrix{Matrix00=color.R/255f,Matrix11=color.G/255f,Matrix22=color.B/255f,Matrix33=color.A/255f};attrs.SetColorMatrix(matrix);
        g.DrawImage(GlowTexture,Rectangle.Round(rect),0,0,GlowTexture.Width,GlowTexture.Height,GraphicsUnit.Pixel,attrs);
    }
    private static Bitmap CreateGlowTexture()
    {
        var bitmap=new Bitmap(192,192,PixelFormat.Format32bppArgb);using var g=Graphics.FromImage(bitmap);g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
        using var path=new GraphicsPath();path.AddEllipse(0,0,191,191);using var brush=new PathGradientBrush(path){CenterColor=Color.White,SurroundColors=new[]{Color.FromArgb(0,255,255,255)}};g.FillPath(brush,path);return bitmap;
    }
    private void DrawLights(Graphics g,RectangleF r,float opacity)
    {
        PointF Point(float x,float y)=>new(r.X+x*r.Width,r.Y+y*r.Height);
        void Lamp(params PointF[] points)
        {
            for(int i=4;i>=1;i--){using var halo=new Pen(Color.FromArgb((int)(opacity*(i==1?90:12)),255,163,67),Math.Max(1,r.Width*(i==1?.01f:.009f*i))){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};g.DrawLines(halo,points);}
            using var core=new Pen(Color.FromArgb((int)(235*opacity),255,237,192),Math.Max(1,r.Width*.0028f)){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};g.DrawLines(core,points);
        }
        if(_model=="ds4")Lamp(Point(.350f,.147f),Point(.650f,.147f));
        else if(_model is "ds5" or "dse")
        {
            float left=_model=="ds5"?.340f:.326f,right=1-left;
            Lamp(Point(left,.118f),Point(left+.011f,.254f),Point(left+.033f,.342f));
            Lamp(Point(right,.118f),Point(right-.011f,.254f),Point(right-.033f,.342f));
        }
        else
        {
            float y=_model switch{"xbox-360"=>.305f,"xbox-one"=>.207f,"xbox-series"=>.199f,_=>.226f};
            float size=_model=="xbox-360"?.045f:.031f;var center=Point(.5f,y);
            Glow(g,new RectangleF(center.X-r.Width*size*1.65f,center.Y-r.Width*size*1.65f,r.Width*size*3.3f,r.Width*size*3.3f),Color.FromArgb((int)(120*opacity),255,177,74));
            float a=r.Width*size*.53f;Lamp(new PointF(center.X-a,center.Y-a*.82f),center,new PointF(center.X+a,center.Y+a*.82f));
            Lamp(new PointF(center.X+a,center.Y-a*.82f),center,new PointF(center.X-a,center.Y+a*.82f));
        }
    }
    protected override void Dispose(bool disposing)
    {if(disposing){_animation.Stop();_animation.Dispose();foreach(var image in _images.Values)image.Dispose();_images.Clear();}base.Dispose(disposing);}
}

internal sealed class StudioCardTable : TableLayoutPanel
{
    public StudioCardTable(){DoubleBuffered=true;BackColor=StudioTheme.Surface;}
    protected override void OnPaintBackground(PaintEventArgs e)=>StudioTheme.PaintCard(e.Graphics,ClientRectangle,BackColor,Parent?.BackColor??StudioTheme.Background);
}
