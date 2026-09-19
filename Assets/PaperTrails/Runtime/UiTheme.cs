using UnityEngine;

namespace PaperTrails
{
    // Procedural UI theme matching the reference mockups: rounded glossy
    // panels/cards/buttons, pill chips, slider track and thumb. Everything is
    // generated once at boot and cached; per-frame cost is plain DrawTextures.
    // The two art assets (menu background, logo) load from Resources/UI.
    public static class UiTheme
    {
        public static Texture2D Panel, Card, BtnRed, BtnGreen, BtnBlue, BtnGray, BtnDisabled;
        public static Texture2D SliderTrack, SliderThumb, Chip, Background, Logo;
        static bool ready;
        static Color32[] Pixels(int w, int h) => new Color32[w * h];
        static void CircleTest(int x, int y, int w, int h, int r, out bool inside, out bool border)
        {
            int dx = x < r ? r - x : (x >= w - r ? x - (w - r - 1) : -1);
            int dy = y < r ? r - y : (y >= h - r ? y - (h - r - 1) : -1);
            if (dx < 0 || dy < 0) { inside = true; border = false; return; }
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            inside = d <= r; border = d > r - 3;
        }
        static Texture2D Rounded(int w, int h, int r, Color32 top, Color32 bottom, Color32 border, byte gloss)
        {
            var px = Pixels(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    CircleTest(x, y, w, h, r, out bool inside, out bool edge);
                    if (!inside) { px[y * w + x] = new Color32(0, 0, 0, 0); continue; }
                    float t = y / (float)(h - 1);
                    var c = Color32.Lerp(top, bottom, t);
                    if (edge) c = border;
                    else if (gloss > 0 && t < .45f)
                    {
                        byte g = (byte)(gloss * (1 - t / .45f));
                        c = Color32.Lerp(c, new Color32(255, 255, 255, c.a), g / 255f);
                    }
                    px[y * w + x] = c;
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            tex.SetPixels32(px); tex.Apply(true); tex.filterMode = FilterMode.Trilinear; tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
        static Texture2D Knob(int s, Color32 face, Color32 rim)
        {
            var px = Pixels(s, s);
            float c = (s - 1) / 2f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    if (d > 1) { px[y * s + x] = new Color32(0, 0, 0, 0); continue; }
                    var col = Color32.Lerp(face, rim, Mathf.Clamp01((d - .55f) / .45f));
                    float gloss = Mathf.Clamp01(1 - d * 1.6f);
                    col = Color32.Lerp(col, new Color32(255, 255, 255, 255), gloss * .5f);
                    px[y * s + x] = col;
                }
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, true);
            tex.SetPixels32(px); tex.Apply(true); tex.filterMode = FilterMode.Trilinear; tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
        public static void Ensure()
        {
            if (ready) return;
            ready = true;
            Panel = Rounded(256, 256, 40, new Color32(23, 42, 74, 246), new Color32(10, 20, 38, 246), new Color32(86, 150, 235, 110), 26);
            Card = Rounded(256, 256, 36, new Color32(28, 52, 92, 235), new Color32(13, 26, 48, 235), new Color32(86, 150, 235, 80), 20);
            BtnRed = Rounded(192, 192, 44, new Color32(255, 70, 95, 255), new Color32(190, 22, 55, 255), new Color32(120, 10, 30, 255), 70);
            BtnGreen = Rounded(192, 192, 44, new Color32(70, 255, 120, 255), new Color32(16, 175, 70, 255), new Color32(8, 110, 40, 255), 70);
            BtnBlue = Rounded(192, 192, 44, new Color32(70, 170, 255, 255), new Color32(20, 100, 220, 255), new Color32(10, 60, 140, 255), 60);
            BtnGray = Rounded(192, 192, 44, new Color32(105, 125, 150, 255), new Color32(55, 68, 85, 255), new Color32(30, 38, 50, 255), 40);
            BtnDisabled = Rounded(192, 192, 44, new Color32(70, 85, 100, 200), new Color32(45, 55, 68, 200), new Color32(30, 38, 50, 160), 0);
            SliderTrack = Rounded(192, 40, 20, new Color32(10, 18, 32, 255), new Color32(16, 28, 48, 255), new Color32(60, 100, 150, 120), 0);
            SliderThumb = Knob(96, new Color32(240, 248, 255, 255), new Color32(90, 180, 255, 255));
            Chip = Rounded(128, 128, 56, new Color32(45, 90, 145, 190), new Color32(25, 50, 90, 190), new Color32(120, 190, 255, 110), 30);
            Background = Resources.Load<Texture2D>("UI/Background");
            Logo = Resources.Load<Texture2D>("UI/Logo");
        }
        public static readonly Color Ink = new Color(.93f, .96f, 1f);
        public static readonly Color Muted = new Color(.62f, .72f, .84f);
        public static readonly Color Gold = new Color(1f, .85f, .3f);
        public static readonly Color Mint = new Color(.25f, 1f, .55f);
        public static GUIStyle CardButton(GUIStyle basis, Texture2D back)
        {
            var s = new GUIStyle(basis) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            s.normal.background = back; s.hover.background = back; s.active.background = back;
            s.normal.textColor = Color.white; s.hover.textColor = Color.white; s.active.textColor = Color.white;
            return s;
        }
    }
}
