using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CODMControllerProbe;

// Each light has its own elliptical support, entirely inside the canvas.
// Never fade against the four canvas edges: that reveals a rectangular panel.
internal static class StudioProductLighting
{
    private static float Smooth(float value) { float t = Math.Clamp(value, 0, 1); return t * t * (3 - 2 * t); }
    private static float Spot(float x, float y, float cx, float cy, float rx, float ry, float angle = 0)
    {
        float dx = x - cx, dy = y - cy;
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        float u = (dx * c + dy * s) / rx, v = (-dx * s + dy * c) / ry;
        float radius = u * u + v * v;
        return radius >= 1 ? 0 : MathF.Exp(-4 * radius) * Smooth((1 - radius) / .35f);
    }
    public static Bitmap Create(Size size, ControllerLightingPalette model, int intensityPercent = 135)
    {
        int width = Math.Max(1, size.Width), height = Math.Max(1, size.Height);
        var pixels = new byte[checked(width * height * 4)];
        float gain = Math.Clamp(intensityPercent, 60, 180) / 100f;
        for (int y = 0; y < height; y++)
        {
            float py = (y + .5f) / height;
            for (int x = 0; x < width; x++)
            {
                float px = (x + .5f) / width;
                // Offset, rotated lights preserve dark space around the subject.
                // Brightness is concentrated at the silhouette, not across a flat rectangle.
                float key = gain * (.34f * Spot(px, py, .39f, .40f, .37f, .36f, -.32f)
                    + .34f * Spot(px, py, .25f, .36f, .18f, .29f, -.18f));
                float fill = gain * (.23f * Spot(px, py, .66f, .46f, .29f, .39f, .36f)
                    + .28f * Spot(px, py, .76f, .51f, .16f, .26f, .18f));
                float air = .025f * Spot(px, py, .50f, .42f, .46f, .40f) * gain;
                float floor = gain * (.19f * Spot(px, py, .50f, .87f, .40f, .085f)
                    + .10f * Spot(px, py, .50f, .86f, .29f, .018f));
                float total = key + fill + air + floor;
                float scale = total > .90f ? .90f / total : 1;
                key *= scale; fill *= scale; air *= scale; floor *= scale;
                float a = Math.Min(.90f, total);
                int i = (y * width + x) * 4;
                pixels[i] = (byte)Math.Clamp(model.Primary.B * (key + floor * .65f) + model.Secondary.B * (fill + floor * .35f) + 185 * air, 0, 255);
                pixels[i + 1] = (byte)Math.Clamp(model.Primary.G * (key + floor * .65f) + model.Secondary.G * (fill + floor * .35f) + 199 * air, 0, 255);
                pixels[i + 2] = (byte)Math.Clamp(model.Primary.R * (key + floor * .65f) + model.Secondary.R * (fill + floor * .35f) + 214 * air, 0, 255);
                pixels[i + 3] = (byte)(255 * a);
            }
        }
        var result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        var data = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try { for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, data.Scan0 + y * data.Stride, width * 4); }
        finally { result.UnlockBits(data); }
        return result;
    }

    public static int RimPadding => Dpi.Px(28);
    public static Bitmap CreateRim(Bitmap product, ControllerLightingPalette model, int intensityPercent)
    {
        int pad = RimPadding, width = product.Width + 2 * pad, height = product.Height + 2 * pad;
        using var silhouette = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(silhouette)) g.DrawImageUnscaled(product, pad, pad);
        var bytes = new byte[width * height * 4];
        var data = silhouette.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try { for (int y = 0; y < height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * width * 4, width * 4); }
        finally { silhouette.UnlockBits(data); }
        var alpha = new float[width * height];
        for (int i = 0; i < alpha.Length; i++) alpha[i] = bytes[i * 4 + 3];
        var soft = Blur(Blur(Blur(alpha, width, height, Dpi.Px(8)), width, height, Dpi.Px(8)), width, height, Dpi.Px(8));
        var near = Blur(Blur(Blur(alpha, width, height, Dpi.Px(3)), width, height, Dpi.Px(3)), width, height, Dpi.Px(3));
        float gain = Math.Clamp(intensityPercent, 60, 180) / 100f;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int p = y * width + x, i = p * 4;
                float blend = Smooth(x / (float)Math.Max(1, width - 1));
                float nx = (x - pad) / (float)Math.Max(1, product.Width), ny = (y - pad) / (float)Math.Max(1, product.Height);
                // Side/back lighting; leave the centre and lower edge in shadow.
                float direction = .16f + .84f * MathF.Pow(Math.Abs(nx - .5f) * 2, 1.5f);
                direction *= 1 - .55f * Smooth((ny - .62f) / .38f);
                float halo = Math.Max(0, soft[p] - alpha[p]) * 1.10f + Math.Max(0, near[p] - alpha[p]) * .90f;
                float a = Math.Clamp(halo * gain * direction, 0, 120) / 255f;
                bytes[i] = (byte)((model.Primary.B * (1 - blend) + model.Secondary.B * blend) * a);
                bytes[i + 1] = (byte)((model.Primary.G * (1 - blend) + model.Secondary.G * blend) * a);
                bytes[i + 2] = (byte)((model.Primary.R * (1 - blend) + model.Secondary.R * blend) * a);
                bytes[i + 3] = (byte)(a * 255);
            }
        var result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        data = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try { for (int y = 0; y < height; y++) Marshal.Copy(bytes, y * width * 4, data.Scan0 + y * data.Stride, width * 4); }
        finally { result.UnlockBits(data); }
        return result;
    }

    private static float[] Blur(float[] input, int width, int height, int radius)
    {
        radius = Math.Max(1, radius); int diameter = radius * 2 + 1;
        var horizontal = new float[input.Length]; var result = new float[input.Length];
        for (int y = 0; y < height; y++)
        {
            float sum = 0;
            for (int x = 0; x < Math.Min(width, radius + 1); x++) sum += input[y * width + x];
            for (int x = 0; x < width; x++)
            {
                horizontal[y * width + x] = sum / diameter;
                if (x - radius >= 0) sum -= input[y * width + x - radius];
                if (x + radius + 1 < width) sum += input[y * width + x + radius + 1];
            }
        }
        for (int x = 0; x < width; x++)
        {
            float sum = 0;
            for (int y = 0; y < Math.Min(height, radius + 1); y++) sum += horizontal[y * width + x];
            for (int y = 0; y < height; y++)
            {
                result[y * width + x] = sum / diameter;
                if (y - radius >= 0) sum -= horizontal[(y - radius) * width + x];
                if (y + radius + 1 < height) sum += horizontal[(y + radius + 1) * width + x];
            }
        }
        return result;
    }
}
