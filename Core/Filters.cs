namespace SamuraiGBA.Core;

public static class Filters
{
    public static readonly (string Key, string Label, int Factor)[] All =
    {
        ("Nearest", "Nearest (razor sharp pixels)", 1),
        ("Smooth",  "Smooth (bilinear)", 1),
        ("Sharp",   "Sharp bilinear (recommended)", 4),
        ("CRT",     "CRT scanlines", 4),
        ("LCD",     "GBA LCD grid", 3),
    };

    public static int FactorOf(string key)
    {
        foreach (var f in All) if (f.Key == key) return f.Factor;
        return 1;
    }

    static int Dim(int p, int m)
        => (((p >> 16) & 255) * m >> 8 << 16) | (((p >> 8) & 255) * m >> 8 << 8) | ((p & 255) * m >> 8);

    static int Tint(int p, int rm, int gm, int bm, int boost)
    {
        int r = Math.Min(255, ((p >> 16) & 255) * rm * boost >> 16);
        int g = Math.Min(255, ((p >> 8) & 255) * gm * boost >> 16);
        int b = Math.Min(255, (p & 255) * bm * boost >> 16);
        return (r << 16) | (g << 8) | b;
    }

    public static void Apply(string key, int[] s, int w, int h, int[] o, int f)
    {
        if (f == 1) { Array.Copy(s, o, w * h); return; }
        int ow = w * f;
        switch (key)
        {
            case "CRT":
            {
                int[] levels = { 256, 256, 215, 130 };
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int p = s[y * w + x];
                        for (int dy = 0; dy < 4; dy++)
                            Array.Fill(o, dy < 2 ? p : Dim(p, levels[dy]), (y * 4 + dy) * ow + x * 4, 4);
                    }
                break;
            }
            case "LCD":
            {
                // per-subpixel columns, darker bottom row, brightness compensated
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int p = s[y * w + x];
                        int c0 = Tint(p, 256, 140, 140, 360), c1 = Tint(p, 140, 256, 140, 360), c2 = Tint(p, 140, 140, 256, 360);
                        for (int dy = 0; dy < 3; dy++)
                        {
                            int i = (y * 3 + dy) * ow + x * 3;
                            if (dy == 2) { o[i] = Dim(c0, 150); o[i + 1] = Dim(c1, 150); o[i + 2] = Dim(c2, 150); }
                            else { o[i] = c0; o[i + 1] = c1; o[i + 2] = c2; }
                        }
                    }
                break;
            }
            default: // "Sharp": integer nearest upscale; WPF's bilinear then smooths only the final fractional scale
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int p = s[y * w + x];
                        for (int dy = 0; dy < f; dy++)
                            Array.Fill(o, p, (y * f + dy) * ow + x * f, f);
                    }
                break;
        }
    }
}
