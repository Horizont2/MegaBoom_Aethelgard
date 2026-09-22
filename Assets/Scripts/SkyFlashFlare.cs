using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The patch of sky a lightning strike lights up.
//
// ==== WHY A CARD AND NOT A LIGHT ====
//
// The obvious way to make a strike light the sky is a very bright light, and it
// does not work in weather. A light illuminates SURFACES; the sky is not one,
// and neither is fog — Pure Volumetric Fog scatters a single directional light
// and nothing else, so a point light hung in a cloud is invisible to it.
//
// Flashing the directional light instead lights every surface in the world at
// once, which is a uniform brightening of every pixel. That is a screen flash,
// and an audience reads it as a post effect rather than as something happening
// in the sky. Lightning reads as lightning because it is LOCAL: brightest at the
// bolt, falling away, so there is a hot patch in one place and everything in
// front of it becomes a silhouette. A source with a direction, which the eye
// picks up instantly.
//
// So the glow is geometry: a camera-facing additive card with a soft falloff,
// hung where the strike is. Being geometry, whatever atmosphere is in the scene
// attenuates it with distance for free — a far strike is a dim smudge and a near
// one fills the sky, without anything having to be told the difference.
//
// Call SkyFlashFlare.Flash(...) from anywhere; it builds and pools its own
// cards and needs nothing set up in the scene.
public class SkyFlashFlare : MonoBehaviour
{
    // Three, because two strikes can overlap and a third is free. One card
    // restarted mid-flash is a flicker, which is the one thing this must not
    // look like.
    private const int Cards = 3;

    private static SkyFlashFlare s_instance;
    private static Material s_material;
    private static Texture2D s_softDot;

    private readonly List<Transform> cards = new List<Transform>();
    private readonly List<Material> mats = new List<Material>();
    private readonly List<bool> busy = new List<bool>();

    /// <summary>A strike lighting the sky at `at`.</summary>
    /// <param name="size">Diameter in metres. Big — this is a cloud, not a lamp.</param>
    /// <param name="strength">How far past the colour it is pushed. Above one it blows out, which is the point.</param>
    public static void Flash(Vector3 at, Color colour, float size, float strength, float seconds)
    {
        if (s_instance == null)
        {
            var go = new GameObject("SkyFlashFlare");
            go.hideFlags = HideFlags.DontSave;
            s_instance = go.AddComponent<SkyFlashFlare>();
        }
        s_instance.Fire(at, colour, size, strength, seconds);
    }

    private void Fire(Vector3 at, Color colour, float size, float strength, float seconds)
    {
        int i = FreeCard();
        if (i < 0) return;

        cards[i].position = at;
        cards[i].localScale = Vector3.one * size;
        busy[i] = true;
        StartCoroutine(Burn(i, colour, size, strength, seconds));
    }

    private int FreeCard()
    {
        for (int i = 0; i < cards.Count; i++)
            if (!busy[i] && cards[i] != null) return i;

        if (cards.Count >= Cards) return -1;
        return Build();
    }

    private int Build()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Flare_" + cards.Count;
        go.hideFlags = HideFlags.DontSave;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.zero;

        var r = go.GetComponent<MeshRenderer>();
        // A copy per card: they fade independently, and they would otherwise
        // fight over one material's colour.
        var m = new Material(SharedMaterial());
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        cards.Add(go.transform);
        mats.Add(m);
        busy.Add(false);
        return cards.Count - 1;
    }

    private IEnumerator Burn(int i, Color colour, float size, float strength, float seconds)
    {
        Transform card = cards[i];
        Material mat = mats[i];
        float t = 0f;

        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(t / Mathf.Max(0.01f, seconds));

            Camera c = Camera.main;
            if (c != null) card.rotation = c.transform.rotation;

            // Grows a little as it dies. A lit cloud spreads; it does not shrink
            // back to the point it started from.
            card.localScale = Vector3.one * size * (1f + (1f - k) * 0.35f);

            // Squared decay: a linear fade reads as a dimmer being turned down,
            // and lightning does not do that.
            Color col = colour * (strength * k * k);
            col.a = 1f;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);

            yield return null;
        }

        card.localScale = Vector3.zero;
        busy[i] = false;
    }

    private static Material SharedMaterial()
    {
        if (s_material != null) return s_material;

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        s_material = new Material(sh) { name = "M_SkyFlashFlare (runtime)" };

        if (s_material.HasProperty("_BaseMap")) s_material.SetTexture("_BaseMap", SoftDot());
        if (s_material.HasProperty("_MainTex")) s_material.SetTexture("_MainTex", SoftDot());
        if (s_material.HasProperty("_Surface")) s_material.SetFloat("_Surface", 1f);
        s_material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        s_material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);   // additive
        s_material.SetInt("_ZWrite", 0);
        s_material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        s_material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return s_material;
    }

    // A quad with no texture is a hard-edged square, which is the whole reason
    // script-built glows look like paper. Squared falloff, because a linear ramp
    // still shows a visible disc edge.
    private static Texture2D SoftDot()
    {
        if (s_softDot != null) return s_softDot;

        const int N = 64;
        s_softDot = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        float c = (N - 1) * 0.5f;
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = Mathf.Clamp01(1f - d); a *= a;
            px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        s_softDot.SetPixels32(px);
        s_softDot.Apply(true);
        return s_softDot;
    }
}
