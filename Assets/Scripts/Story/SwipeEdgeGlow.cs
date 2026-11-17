using UnityEngine;
using UnityEngine.UI;

namespace Story
{
    /// <summary>
    /// Procedural screen-edge glow for the phone-call swipe indicators.
    /// Grimy / melancholic look: a dim, desaturated vignette-style gradient that fades from a dirty tint
    /// into murky darkness, with slow drifting dust, ash flakes and faint window-rain.
    /// Leaning toward a side only gently intensifies it and pulls the dust inward.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class SwipeEdgeGlow : MaskableGraphic
    {
        public bool isLeft = true;
        [Range(0f, 1f)] public float strength = 0f;

        private const int Cols = 48;
        private const int Rows = 12;
        private const int DustCount = 38;
        private const int RainCount = 7;

        protected override void OnEnable()
        {
            base.OnEnable();
            raycastTarget = false;
        }

        private void Update()
        {
            SetVerticesDirty();
        }

        private static float Hash(float n)
        {
            float s = Mathf.Sin(n * 12.9898f) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect r = rectTransform.rect;
            float w = r.width;
            float h = r.height;
            if (w < 2f || h < 2f) return;

            float dir = isLeft ? 1f : -1f;
            float x0 = isLeft ? r.xMin : r.xMax;
            Color tint = color;
            float time = Time.unscaledTime;

            // Slow "tired" breathing plus a rare faulty-light flicker
            float breathe = 0.93f + 0.07f * Mathf.Sin(time * 0.9f + (isLeft ? 0f : 2.1f));
            float flickerGate = Mathf.Sin(time * 0.37f + (isLeft ? 1.3f : 4.0f));
            float flicker = flickerGate > 0.965f ? 0.82f + 0.18f * Mathf.Sin(time * 47f) : 1f;
            float intensity = Mathf.Lerp(0.78f, 1f, strength) * breathe * flicker;

            Color dirtyEdge = Color.Lerp(tint, new Color(0.55f, 0.52f, 0.46f, 1f), 0.18f); // faint nicotine-grey cast
            Color murk = Color.Lerp(tint, new Color(0.02f, 0.03f, 0.04f, 1f), 0.78f);       // fades into dark, not into bright

            // ---------- Gradient body: dirty tint -> murky dark -> nothing ----------
            for (int ix = 0; ix <= Cols; ix++)
            {
                float t = (float)ix / Cols;
                float x = x0 + dir * t * w;

                // Holds strong through the label area, then a long smooth tail to full transparency (zero slope at the end)
                float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 1f, t));
                // Colour sinks from the tint into murk as it fades inward
                Color c = Color.Lerp(dirtyEdge, murk, Mathf.SmoothStep(0f, 1f, t * 1.05f));

                for (int iy = 0; iy <= Rows; iy++)
                {
                    float v = (float)iy / Rows;
                    float y = r.yMin + v * h;

                    // Vignette: weaker toward top and bottom corners so it feels like a heavy shadow, not a bar
                    float edgeV = Mathf.Abs(2f * v - 1f);
                    float vy = 1f - Mathf.Pow(edgeV, 2.2f) * 0.65f;

                    // Slow uneven stains drifting through the gradient (grime, not a clean ramp)
                    float stain = 0.88f + 0.12f * Mathf.Sin(v * 7.3f + time * 0.35f + t * 4.0f)
                                        * Mathf.Sin(v * 3.1f - time * 0.22f);

                    UIVertex vert = UIVertex.simpleVert;
                    vert.position = new Vector3(x, y, 0f);
                    vert.color = new Color(c.r, c.g, c.b, tint.a * a * vy * stain * intensity * 0.92f);
                    vh.AddVert(vert);
                }
            }

            for (int ix = 0; ix < Cols; ix++)
            {
                for (int iy = 0; iy < Rows; iy++)
                {
                    int i0 = ix * (Rows + 1) + iy;
                    int i1 = i0 + 1;
                    int i2 = i0 + Rows + 2;
                    int i3 = i0 + Rows + 1;
                    vh.AddTriangle(i0, i1, i2);
                    vh.AddTriangle(i0, i2, i3);
                }
            }

            // Pale, desaturated particle colour (dust catching dim light)
            Color dustC = Color.Lerp(tint, new Color(0.72f, 0.70f, 0.64f, 1f), 0.62f);
            float pull = 1f + strength * 1.1f;

            // ---------- Floating dust & ash: slow, sinking, lazily swaying ----------
            for (int i = 0; i < DustCount; i++)
            {
                float s1 = Hash(i + 1f);
                float s2 = Hash(i + 61f);
                float s3 = Hash(i + 121f);
                float s4 = Hash(i + 181f);
                float s5 = Hash(i + 241f);

                float cycle = Mathf.Lerp(9f, 17f, s2);                 // long, lazy lifetimes
                float p = Mathf.Repeat(time / cycle + s1, 1f);

                // Cluster toward the edge (squared bias), then creep inward slowly
                float baseX = Mathf.Pow(s3, 1.9f) * w * 0.75f;
                float x = x0 + dir * (baseX + p * w * 0.10f * pull);

                // Sinks downward with a gentle sway (nothing rises, nothing sparkles)
                float y = r.yMax - (0.05f + 0.70f * s4 + p * 0.25f) * h;
                y += Mathf.Sin(time * 0.6f + s1 * 6.2831f) * 6f;
                x += Mathf.Sin(time * 0.45f + s5 * 6.2831f) * 5f;

                // Fade in and out softly, and dim "breathing" per speck
                float life = Mathf.Sin(p * Mathf.PI);
                float dim = 0.7f + 0.3f * Mathf.Sin(time * 1.1f + s5 * 9f);
                float alpha = life * dim * Mathf.Lerp(0.14f, 0.40f, s2);

                // Mostly tiny specks, a few larger flakes of ash
                float size = s5 > 0.88f ? Mathf.Lerp(3.5f, 5.5f, s4) : Mathf.Lerp(1.3f, 2.8f, s4);
                float rot = s1 * 6.2831f + time * (s2 - 0.5f) * 0.6f;
                float stretch = s5 > 0.88f ? Mathf.Lerp(0.5f, 0.9f, s3) : 1f;

                AddRotatedQuad(vh, x, y, size, size * stretch, rot,
                    new Color(dustC.r, dustC.g, dustC.b, dustC.a * alpha * (0.85f + 0.15f * strength)));
            }

            // ---------- Faint window-rain: thin, slow, barely there ----------
            for (int i = 0; i < RainCount; i++)
            {
                float s1 = Hash(i + 301f);
                float s2 = Hash(i + 361f);
                float s3 = Hash(i + 421f);

                float cycle = Mathf.Lerp(3.8f, 6.5f, s2);
                float p = Mathf.Repeat(time / cycle + s1, 1f);

                float x = x0 + dir * (w * (0.04f + 0.30f * s3));
                float len = Mathf.Lerp(26f, 70f, s2);
                float yTop = r.yMax + len - p * (h + len * 2f);

                float life = Mathf.Sin(p * Mathf.PI);
                float alpha = life * Mathf.Lerp(0.07f, 0.16f, s3);

                // Tapered drip: bright head, fading tail upward
                Color head = new Color(dustC.r, dustC.g, dustC.b, dustC.a * alpha);
                Color tail = new Color(dustC.r, dustC.g, dustC.b, 0f);
                int start = vh.currentVertCount;
                UIVertex v = UIVertex.simpleVert;
                float hw = 0.8f;
                v.position = new Vector3(x - hw, yTop + len, 0f); v.color = tail; vh.AddVert(v);
                v.position = new Vector3(x + hw, yTop + len, 0f); v.color = tail; vh.AddVert(v);
                v.position = new Vector3(x + hw * 1.4f, yTop, 0f); v.color = head; vh.AddVert(v);
                v.position = new Vector3(x - hw * 1.4f, yTop, 0f); v.color = head; vh.AddVert(v);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
        }

        private static void AddRotatedQuad(VertexHelper vh, float cx, float cy, float halfW, float halfH, float angle, Color col)
        {
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            int start = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.color = col;

            Vector2[] corners =
            {
                new Vector2(-halfW, -halfH),
                new Vector2(-halfW,  halfH),
                new Vector2( halfW,  halfH),
                new Vector2( halfW, -halfH)
            };
            for (int i = 0; i < 4; i++)
            {
                float px = corners[i].x * cos - corners[i].y * sin;
                float py = corners[i].x * sin + corners[i].y * cos;
                v.position = new Vector3(cx + px, cy + py, 0f);
                vh.AddVert(v);
            }
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
