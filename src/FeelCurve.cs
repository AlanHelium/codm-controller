namespace CODMControllerProbe;

/// <summary>
/// 手感响应曲线：输入推幅(0~1) → 输出推幅(0~1) 的平滑映射。
/// - 控制点（X 单调递增）+ 每点切线（对 X 的导数）的 Hermite 分段插值：
///   曲线平滑经过每个控制点（Catmull-Rom 同款切线，一阶连续）。
/// - 反死区即曲线起点高度：轻推刚出滤噪死区就直接输出反死区推量，游戏内置死区形同虚设。
/// - 加点（AddPoint）不会改变曲线形状：新点落在原曲线上、切线取原曲线在该点的导数，
///   其余点切线保持不变 —— 数学上逐段与原曲线完全一致，只是多出一个可拖的控制点。
/// - 拖动点（SetPoint）/预设/删点/改反死区后重新计算切线，形状自然重排。
/// 免费版锁定为出厂预设；Pro 版开放拖动编辑（本窗口为调试/Pro 形态）。
/// </summary>
public class FeelCurve
{
    /// <summary>控制点列表：X 单调递增，首点 X=0、末点 X=1。</summary>
    public List<PointF> Points = new();

    /// <summary>每点切线（对 X 的导数 dy/dx），与 Points 一一对应。加点时局部保留（见 AddPoint）。</summary>
    private readonly List<float> _tans = new();

    /// <summary>反死区（0~0.5）：始终等于首点高度，滑杆修改时同步。</summary>
    public float Anti
    {
        get => Points.Count > 0 ? Points[0].Y : 0f;
        set
        {
            if (Points.Count > 0)
            {
                var p = Points[0];
                Points[0] = new PointF(p.X, Math.Clamp(value, 0f, 0.5f));
                RecalcTans();
            }
        }
    }

    /// <summary>横向（X 轴）灵敏度 0.5~2.0，默认 1.0。FPS 常见：X 调高转视角快。</summary>
    public float GainX = 1f;

    /// <summary>纵向（Y 轴）灵敏度 0.5~2.0，默认 1.0。FPS 常见：Y 调低瞄准微调稳。</summary>
    public float GainY = 1f;

    // ---- 切线（Catmull-Rom：中心差分，端点单侧差分）----

    private void RecalcTans()
    {
        _tans.Clear();
        int n = Points.Count;
        if (n == 0) return;
        if (n == 1) { _tans.Add(0f); return; }
        for (int i = 0; i < n; i++)
        {
            if (i == 0)
                _tans.Add((Points[1].Y - Points[0].Y) / (Points[1].X - Points[0].X));
            else if (i == n - 1)
                _tans.Add((Points[n - 1].Y - Points[n - 2].Y) / (Points[n - 1].X - Points[n - 2].X));
            else
                _tans.Add((Points[i + 1].Y - Points[i - 1].Y) / (Points[i + 1].X - Points[i - 1].X));
        }
    }

    /// <summary>求输入 x(0~1) 对应的输出推幅(0~1)，Hermite 分段平滑过点。</summary>
    public float Eval(float x)
    {
        if (Points.Count < 2) return x;
        if (x <= Points[0].X) return Points[0].Y;
        if (x >= Points[^1].X) return Points[^1].Y;
        if (_tans.Count != Points.Count) RecalcTans();

        int i = 0;
        for (int k = 0; k < Points.Count - 1; k++)
        {
            if (x >= Points[k].X && x <= Points[k + 1].X) { i = k; break; }
        }

        var p0 = Points[i];
        var p1 = Points[i + 1];
        float m0 = _tans[i];
        float m1 = _tans[i + 1];
        float h = p1.X - p0.X;
        float t = (x - p0.X) / h;      // 0~1
        float t2 = t * t, t3 = t2 * t;
        // Hermite 基函数
        float h00 = 2f * t3 - 3f * t2 + 1f;
        float h10 = t3 - 2f * t2 + t;
        float h01 = -2f * t3 + 3f * t2;
        float h11 = t3 - t2;
        return h00 * p0.Y + h10 * h * m0 + h01 * p1.Y + h11 * h * m1;
    }

    /// <summary>曲线在某点的导数（数值差分，用于加点时取新点切线）。</summary>
    private float Deriv(float x)
    {
        const float h = 1e-4f;
        return (Eval(x + h) - Eval(x - h)) / (2f * h);
    }

    /// <summary>
    /// 等比缩放副本：起点（反死区高度，X=0 第一个控制点）锚定不动，
    /// 起点到终点的输出区间 [起点, 1] 按档位整体压缩到 [起点, s]，X 不变。
    /// 保证单调递增（起点之后不会低于起点）、形状坡度保持，只改变输出极值。
    /// </summary>
    public FeelCurve Scaled(float s)
    {
        var c = new FeelCurve { GainX = GainX, GainY = GainY };
        float anti = Points.Count > 0 ? Points[0].Y : 0f;
        float span = 1f - anti;
        float newSpan = Math.Max(s - anti, 0f);
        foreach (var p in Points)
        {
            float y = span <= 0f ? anti : anti + (p.Y - anti) * newSpan / span;
            c.Points.Add(new PointF(p.X, Math.Clamp(y, 0f, 1f)));
        }
        c.RecalcTans();
        return c;
    }

    /// <summary>
    /// 就地替换为"另一曲线 × 缩放系数"的形态：对象引用不变（画布/转发无需换对象），
    /// 免费版切档位时由 MainForm 调用，保持当前画布和转发同步。
    /// 起点（反死区高度）锚定不动、死区起点与最高档一致；起点到终点的输出区间
    /// 按档位压缩到 [起点, s]，曲线始终单调递增、坡度比例不变。
    /// </summary>
    public void LoadScaledFrom(FeelCurve src, float s)
    {
        Points.Clear();
        float anti = src.Points.Count > 0 ? src.Points[0].Y : 0f;
        float span = 1f - anti;                       // 原始输出区间 [anti,1]
        float newSpan = Math.Max(s - anti, 0f);       // 目标输出区间 [anti,s]
        foreach (var p in src.Points)
        {
            float y = span <= 0f ? anti : anti + (p.Y - anti) * newSpan / span;
            Points.Add(new PointF(p.X, Math.Clamp(y, 0f, 1f)));
        }
        GainX = src.GainX;
        GainY = src.GainY;
        RecalcTans();
    }

    /// <summary>采样曲线为折线点集（供绘制）。</summary>
    public List<PointF> Sample(int n = 101)
    {
        var pts = new List<PointF>(n);
        for (int i = 0; i < n; i++) { float x = i / (float)(n - 1); pts.Add(new PointF(x, Eval(x))); }
        return pts;
    }

    /// <summary>拖动控制点：x 保持单调递增、y 限幅 0~1，随后重算切线。</summary>
    public void SetPoint(int index, PointF p)
    {
        if (index == 0) { /* 起点 X 固定 0，只随反死区滑杆改 Y */ return; }
        float xMin = index > 0 ? Points[index - 1].X + 0.02f : 0f;
        float xMax = index < Points.Count - 1 ? Points[index + 1].X - 0.02f : 1f;
        float y = Math.Clamp(p.Y, 0f, 1f);
        if (index == Points.Count - 1)
            Points[index] = new PointF(1f, y); // 终点 X 固定 1
        else
            Points[index] = new PointF(Math.Clamp(p.X, xMin, xMax), y);
        RecalcTans();
    }

    /// <summary>
    /// 新增一个中间控制点：落在原曲线上（x=段中点、y=Eval(x)、切线=Deriv(x)），
    /// 其余控制点及其切线保持不变 —— 曲线形状严格不变，只是多出一个可拖点。
    /// </summary>
    public void AddPoint()
    {
        if (Points.Count >= 6) return; // 最多 6 个点
        if (Points.Count < 2) { Points.Add(new PointF(0.5f, 0.5f)); RecalcTans(); return; }

        // 找离 0.5 最近的段中点
        int bestIdx = 1; float bestX = 0.5f;
        for (int k = 1; k < Points.Count; k++)
        {
            float mid = (Points[k - 1].X + Points[k].X) / 2f;
            if (MathF.Abs(mid - 0.5f) < MathF.Abs(bestX - 0.5f)) { bestX = mid; bestIdx = k; }
        }

        // 记录加点的位置与切线，其余切线不动 → 逐段与原曲线一致
        float yq = Eval(bestX);
        float mq = Deriv(bestX);
        Points.Insert(bestIdx, new PointF(bestX, yq));
        _tans.Insert(bestIdx, mq);
    }

    /// <summary>移除最中间的中间控制点（至少保留首末两点）。</summary>
    public void RemovePoint()
    {
        if (Points.Count <= 2) return;
        int rm = -1; float best = 1f;
        for (int k = 1; k < Points.Count - 1; k++)
        {
            float d = MathF.Abs(Points[k].X - 0.5f);
            if (d < best) { best = d; rm = k; }
        }
        if (rm > 0) Points.RemoveAt(rm);
        RecalcTans();
    }

    /// <summary>
    /// 把控制点补足到至少 min 个（默认 5 个：含首末 + 3 个中间点）。
    /// 新点落在原曲线上（取当前最宽段中点、Eval 高度、Deriv 切线），
    /// 其余点与切线不动 —— 形状严格不变，只是多出可拖动的控制点。
    /// 解决"两点直线母版无中间点可拖"的问题。
    /// </summary>
    public void EnsurePointCount(int min = 5)
    {
        if (Points.Count >= min) return;
        int guard = 0;
        while (Points.Count < min && guard++ < 12)
        {
            if (Points.Count < 2) { Points.Add(new PointF(0.5f, 0.5f)); continue; }
            int best = 1; float bestLen = -1f;
            for (int k = 1; k < Points.Count; k++)
            {
                float len = Points[k].X - Points[k - 1].X;
                if (len > bestLen) { bestLen = len; best = k; }
            }
            float midX = (Points[best - 1].X + Points[best].X) / 2f;
            float yq = Eval(midX);
            float mq = Deriv(midX);
            Points.Insert(best, new PointF(midX, yq));
            if (_tans.Count >= Points.Count - 1) _tans.Insert(best, mq);
            else RecalcTans();
        }
        RecalcTans();
    }

    /// <summary>
    /// 就地替换为"另一曲线的形状 × 缩放系数"：对象引用不变（画布/转发无需换对象）。
    /// 起点（反死区高度）锚定不动，输出区间按 scale 压缩；保留本曲线的 GainX/GainY
    /// （横纵灵敏度属于单摇杆精调，不随主曲线派生改变）。用于总曲线（Master）→ 各摇杆派生。
    /// </summary>
    public void LoadShapeFrom(FeelCurve src, float scale)
    {
        Points.Clear();
        float anti = src.Points.Count > 0 ? src.Points[0].Y : 0f;
        float span = 1f - anti;
        float newSpan = Math.Max(scale - anti, 0f);
        foreach (var p in src.Points)
        {
            float y = span <= 0f ? anti : anti + (p.Y - anti) * newSpan / span;
            Points.Add(new PointF(p.X, Math.Clamp(y, 0f, 1f)));
        }
        RecalcTans();
    }

    /// <summary>克隆形状（不含 Gain）：用于总曲线视图的初始主曲线（跟随当前右腰射手感）。</summary>
    public static FeelCurve CloneShape(FeelCurve src)
    {
        var c = new FeelCurve();
        foreach (var p in src.Points) c.Points.Add(p);
        c.RecalcTans();
        return c;
    }

    // ---- 档位系数（总曲线母版派生用）----

    /// <summary>腰射档位系数（1~20 档，level 0..19）：y = 0.32 + 0.68×(k/19)^1.35。
    /// 按用户体感重映射 v2：整体范围上调（此前 12 档仅 45%，体感只有正作 2~3 档，太慢）。
    /// 新分布：最低档 32%（不再是 15% 过低）、12 档 ≈ 64%（对应正作 6~8 手感）、最高 100%。
    /// 指数 1.35（原 1.89）：低档段更早加速、中高档更饱满，曲线仍单调递增、高档极速不变。</summary>
    public static float FreeSensScale(int level) => 0.32f + 0.68f * MathF.Pow(Math.Clamp(level, 0, 19) / 19f, 1.35f);

    /// <summary>开镜档位系数（1~20 档）：y = 0.65 + 0.35×(k/19)^1.35。
    /// 随腰射同步上调：最低 65%（原 55%）、第 12 档 ≈ 82%（原 72%）、最高 100%。
    /// 开镜比腰射整体高出约 20 个百分点，保持"开镜更稳、拉枪更细腻"的相对关系。</summary>
    public static float AdsSensScale(int level) => 0.65f + 0.35f * MathF.Pow(Math.Clamp(level, 0, 19) / 19f, 1.35f);

    // ---- 预设工厂 ----

    private static FeelCurve FromPoints(float anti, params (float x, float y)[] pts)
    {
        var c = new FeelCurve();
        c.Points.Add(new PointF(0f, anti));
        foreach (var (x, y) in pts) c.Points.Add(new PointF(x, y));
        c.Points.Add(new PointF(1f, 1f));
        c.RecalcTans();
        return c;
    }

    /// <summary>线性：起点=反死区，终点=1，两点直线。</summary>
    public static FeelCurve Linear(float anti) => FromPoints(anti);

    /// <summary>均衡（小输入略放大、响应自然）：y = x^1.15，介于线性与沉稳之间，适合多数玩家默认。</summary>
    public static FeelCurve Balanced(float anti) => FromPower(anti, 1.15f);

    /// <summary>沉稳（小输入精细，瞄准向）：y = x^1.5，轻推精细、大推才快转。</summary>
    public static FeelCurve Precise(float anti) => FromPower(anti, 1.5f);

    /// <summary>COD20 风格（跟手偏快）：y = x^0.85，小输入放大、响应直接，适合快速反应。</summary>
    public static FeelCurve Cod20(float anti) => FromPower(anti, 0.85f);

    /// <summary>COD21 风格（S 形：轻推精细 + 中段加速）：三段过点，转身快、压枪稳。</summary>
    public static FeelCurve Cod21(float anti) => FromPoints(anti, (0.25f, 0.16f), (0.5f, 0.52f), (0.75f, 0.84f));

    /// <summary>COD22 风格（平滑沉稳）：响应温和、全段可控，适合精细瞄准向。</summary>
    public static FeelCurve Cod22(float anti) => FromPoints(anti, (0.3f, 0.24f), (0.55f, 0.5f), (0.8f, 0.82f));

    private static FeelCurve FromPower(float anti, float p)
    {
        var c = new FeelCurve();
        c.Points.Add(new PointF(0f, anti));
        float[] xs = { 0.2f, 0.4f, 0.6f, 0.8f, 1f };
        foreach (var x in xs) c.Points.Add(new PointF(x, MathF.Pow(x, p)));
        c.RecalcTans();
        return c;
    }
}
