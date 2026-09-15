using UnityEngine;

// A round, soft-edged dot for anything that draws billboarded particles.
//
// ==== WHY THIS KEEPS BEING NEEDED ====
//
// A particle material with no texture assigned does not draw nothing. Unity
// substitutes a solid white pixel, and a billboarded quad stretched over a solid
// pixel is a SQUARE. Every unlit particle system built in script in this project
// has shipped that way at least once, and it reads instantly as programmer art —
// the ash off a dying skeleton and the motes off a reliquary chest were both
// reported, in the same words, as looking like Minecraft.
//
// The fix is one small texture, and there is no reason for each effect to draw
// its own. Built once, shared, and marked DontSave so a scene change cannot
// strand it.
public static class SoftParticleTexture
{
    private static Texture2D s_dot;

    // Bright core fading to fully transparent before the edge, so the quad's own
    // corners are empty and no square can show however bright the tint is.
    public static Texture2D Dot
    {
        get
        {
            if (s_dot != null) return s_dot;

            const int S = 48;
            s_dot = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                name = "SoftParticleDot",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var px = new Color[S * S];
            float c = (S - 1) * 0.5f;
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    // Squared, with a little shoulder: keeps the core tight and
                    // lets the edge disappear rather than ending on a ring.
                    a = a * a * (0.35f + 0.65f * a);
                    px[y * S + x] = new Color(1f, 1f, 1f, a);
                }
            }
            s_dot.SetPixels(px);
            s_dot.Apply();
            return s_dot;
        }
    }
}
