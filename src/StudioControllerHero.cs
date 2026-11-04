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
    private bool _connected, _conversionActive;
    private Bitmap? _stageLight, _deviceFrame, _rimLight;
    private string _stageModel="",_frameModel="";

    private bool _ambientEnabled=true;
    [System.ComponentModel.DefaultValue(true)]
    internal bool AmbientEnabled {get=>_ambientEnabled;set{if(_ambientEnabled==value)return;_ambientEnabled=value;Invalidate();}}
    private int _lightingIntensity=130;
    [System.ComponentModel.DefaultValue(130)]
    internal int LightingIntensity
    {
        get=>_lightingIntensity;
        set { int next=Math.Clamp(value,60,180);if(next==_lightingIntensity)return;_lightingIntensity=next;_stageLight?.Dispose();_stageLight=null;_rimLight?.Dispose();_rimLight=null;Invalidate(); }
    }

    public StudioControllerHero()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);
        BackColor=StudioTheme.Background;AccessibleName=Lang.T("手柄连接状态图","Controller connection illustration");
        _animation.Tick+=(_,_)=>{UpdateBrightness();Invalidate();UpdateAnimation();};
    }
    internal void SetController(ControllerVisual visual, bool conversionActive)
    {
        bool active=visual.Connected&&conversionActive;
        AccessibleDescription=visual.Title+(visual.Connected?Lang.T("，已连接","; connected"):Lang.T("，未连接","; disconnected"))
            +(active?Lang.T("，转换运行中","; conversion active"):Lang.T("，转换未运行","; conversion inactive"));
        if(visual.Asset==_model&&_connected==visual.Connected&&_conversionActive==active)return;
        if(visual.Asset!=_model){_previous=_model;_model=visual.Asset;_modelTick=Environment.TickCount64;}
        _connected=visual.Connected;
        if(_conversionActive!=active){UpdateBrightness();_from=_brightness;_conversionActive=active;_target=active?1:0;_transitionTick=Environment.TickCount64;}
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
        bool animate=visible&&(Math.Abs(_brightness-_target)>.002f||Environment.TickCount64-_modelTick<300);
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
        UpdateBrightness();float light=_brightness;
        bool branded=false;var currentImage=GetImage(_model);
        float aspect=branded?currentImage.Width/(float)currentImage.Height:1.5f;
        // Keep the product at the previous visual scale even in a taller viewport.
        float maxHeight=Dpi.Px(branded?210:220);
        float width=Math.Min(Width*.85f,Math.Min(maxHeight,Height*(branded?.77f:.94f))*aspect),height=width/aspect;
        var imageRect=new RectangleF((Width-width)/2,(Height-height)/2-(branded?Dpi.Px(9):0),width,height);
        if(_ambientEnabled&&light>.003f)
        {
            if(_stageLight==null||_stageLight.Size!=ClientSize||_stageModel!=_model)
            {
                _stageLight?.Dispose();_stageLight=StudioProductLighting.Create(ClientSize,ControllerLightingPalette.Default,_lightingIntensity);_stageModel=_model;
            }
            DrawController(g,_stageLight,ClientRectangle,light);
        }
        // Separating the projected shadow from the body creates a floating product stage.
        if(_ambientEnabled&&light>.003f)
        {
            float floorY=Math.Min(Height-Dpi.Px(12),imageRect.Bottom+height*.08f);
            Glow(g,new RectangleF(imageRect.X+width*.06f,floorY-height*.06f,width*.88f,height*.12f),Color.FromArgb((int)(105*light),0,0,0));
            Glow(g,new RectangleF(imageRect.X+width*.25f,floorY-height*.018f,width*.50f,height*.036f),Color.FromArgb((int)(85*light),0,0,0));
        }
        var frameSize=new Size(Math.Max(1,(int)Math.Round(width)),Math.Max(1,(int)Math.Round(height)));
        if(_deviceFrame==null||_deviceFrame.Size!=frameSize||_frameModel!=_model)
        {
            _deviceFrame?.Dispose();_deviceFrame=new Bitmap(frameSize.Width,frameSize.Height,PixelFormat.Format32bppPArgb);
            using(var draw=Graphics.FromImage(_deviceFrame))
            {
                draw.InterpolationMode=InterpolationMode.HighQualityBicubic;
                using var wrap=new ImageAttributes();wrap.SetWrapMode(WrapMode.TileFlipXY);
                draw.DrawImage(currentImage,new Rectangle(Point.Empty,frameSize),0,0,currentImage.Width,currentImage.Height,GraphicsUnit.Pixel,wrap);
            }
            _frameModel=_model;
            _rimLight?.Dispose();_rimLight=null;
        }
        float switchProgress=Math.Clamp((Environment.TickCount64-_modelTick)/260f,0,1);
        if(_ambientEnabled&&light>.003f)
        {
            _rimLight??=StudioProductLighting.CreateRim(_deviceFrame,ControllerLightingPalette.Default,_lightingIntensity);
            int pad=StudioProductLighting.RimPadding;
            DrawController(g,_rimLight,new RectangleF(imageRect.X-pad,imageRect.Y-pad,_rimLight.Width,_rimLight.Height),light*switchProgress);
        }
        float pictureOpacity=_connected?1:.72f;
        if(_previous.Length>0&&switchProgress<1)DrawController(g,GetImage(_previous),imageRect,(1-switchProgress)*pictureOpacity);
        DrawController(g,_deviceFrame,new Rectangle((int)Math.Round(imageRect.X),(int)Math.Round(imageRect.Y),frameSize.Width,frameSize.Height),(_previous.Length>0?switchProgress:1)*pictureOpacity);
        if(!branded&&_ambientEnabled&&light>.003f)DrawLights(g,imageRect,light*(_previous.Length>0?switchProgress:1));
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
    {if(disposing){_animation.Stop();_animation.Dispose();foreach(var image in _images.Values)image.Dispose();_images.Clear();_stageLight?.Dispose();_deviceFrame?.Dispose();_rimLight?.Dispose();}base.Dispose(disposing);}
}

internal sealed class StudioCardTable : TableLayoutPanel
{
    public StudioCardTable(){DoubleBuffered=true;BackColor=StudioTheme.Surface;}
    protected override void OnPaintBackground(PaintEventArgs e)=>StudioTheme.PaintCard(e.Graphics,ClientRectangle,BackColor,Parent?.BackColor??StudioTheme.Background);
}
