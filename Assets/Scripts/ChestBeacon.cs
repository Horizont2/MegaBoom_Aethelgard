using System.Collections;
using UnityEngine;

// The light that says "there is something here".
//
// ==== WHY THIS IS NOT A LASER INTO THE SKY ====
//
// The old caches put a coloured shaft up to the clouds, and that was rejected for
// good reason: a beam tall enough to see from anywhere is a HUD marker wearing a
// costume. It tells the player the designer put a reward there, not that somebody
// built something there, and once a map has a few the world reads as a menu.
//
// So this is light coming OFF the chest, not a signal fired out of it. A soft
// shaft about two metres tall, a glow on the ground, a point light and a few
// motes drifting up. From thirty metres it is a coloured smudge in the trees that
// makes you turn; from two hundred it is nothing, and you have to actually find
// the place. The colour is the only part that carries information, and it carries
// the one thing worth knowing before you commit to the fight: how good it is.
//
// Everything is generated — mesh, texture, motes — so there is no VFX asset to
// author, nothing to wire, and it works on a chest dropped into any scene. The
// one thing it needs from outside is a transparent material, because Shader.Find
// on a URP package shader is not reliable in a build.
[DisallowMultipleComponent]
public class ChestBeacon : MonoBehaviour
{
    [Header("Look")]
    public Color colour = new Color(0.45f, 1f, 0.55f);
    [Tooltip("Height of the shaft in metres. Deliberately short — see the class note. This is an aura, not a beam.")]
    public float height = 2.2f;
    public float baseRadius = 0.34f;
    [Tooltip("How much the shaft flares out towards the top. Slight: a strong flare reads as a spotlight pointing the wrong way.")]
    public float topRadius = 0.78f;
    public float spinSpeed = 9f;
    [Tooltip("Brightness swing of the slow breathe. Motion is what peripheral vision reacts to — a shaft standing perfectly still is a rock.")]
    [Range(0f, 0.6f)] public float pulse = 0.22f;

    [Header("Fade out")]
    [Tooltip("Seconds the light takes to die once the chest is opened. It has to go: a beacon over a chest the player already emptied sends them back to it.")]
    public float fadeTime = 0.8f;

    private Material _mat;
    private Material _moteMat;
    private MeshRenderer _shaft;
    private Light _light;
    private Transform _motes;
    private float _baseAlpha = 1f;
    private float _lightBase = 1.6f;
    private bool _dying;

    // Builds a beacon over a chest and returns it. `template` is the transparent
    // material to instance — see the class note on why it is passed in.
    public static ChestBeacon Attach(Transform anchor, Color colour, float height, Material template)
    {
        if (anchor == null) return null;
        if (template == null)
        {
            Debug.LogWarning("[ChestBeacon] No beam material supplied, so no beacon was built. Run " +
                             "Tools > Exploration > Build Reliquary Set to pick up LightBeam_Mat.", anchor);
            return null;
        }

        var go = new GameObject("Beacon");
        go.transform.SetParent(anchor, false);
        go.transform.localPosition = Vector3.zero;

        var b = go.AddComponent<ChestBeacon>();
        b.colour = colour;
        b.height = height;
        b.Build(template);
        return b;
    }

    private void Build(Material template)
    {
        _mat = new Material(template);
        _mat.mainTexture = ShaftTexture();
        if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", _mat.mainTexture);
        // Both sides: the shaft is an open cone, so the far wall has to render or
        // it reads as half a cone whenever the camera swings past.
        if (_mat.HasProperty("_Cull")) _mat.SetFloat("_Cull", 0f);
        SetTint(1f);

        // ==== THE MOTES WERE LITERAL SQUARES ====
        //
        // They shared the shaft's material and sampled a 0.16-by-0.16 window of
        // its gradient — a patch where the alpha runs from about 0.3 to 0.7 and
        // never reaches zero. So each mote drew as a solid rectangle with a
        // slight gradient across it, hard-edged against the sky. That is the
        // Minecraft look, and it was the texture, not the geometry: the quads
        // are already billboarded.
        //
        // Their own material, with a round falloff to fully transparent at the
        // rim, so what floats out of the chest is a spark rather than a brick.
        _moteMat = new Material(template);
        _moteMat.mainTexture = SoftParticleTexture.Dot;
        if (_moteMat.HasProperty("_BaseMap")) _moteMat.SetTexture("_BaseMap", _moteMat.mainTexture);
        if (_moteMat.HasProperty("_Cull")) _moteMat.SetFloat("_Cull", 0f);
        OwnedMaterial.Attach(gameObject, _moteMat);

        var shaftGo = new GameObject("Shaft");
        shaftGo.transform.SetParent(transform, false);
        shaftGo.AddComponent<MeshFilter>().sharedMesh = ShaftMesh();
        _shaft = shaftGo.AddComponent<MeshRenderer>();
        _shaft.sharedMaterial = _mat;
        _shaft.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _shaft.receiveShadows = false;
        // Never light-probe a self-illuminated effect; it just costs time.
        _shaft.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = Vector3.up * (height * 0.35f);
        _light = lightGo.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = colour;
        _light.range = 7f;
        _light.intensity = _lightBase;
        // A dozen shadow-casting point lights across a region would cost more than
        // the whole feature is worth.
        _light.shadows = LightShadows.None;

        _motes = new GameObject("Motes").transform;
        _motes.SetParent(transform, false);
        for (int i = 0; i < 5; i++)
        {
            var m = new GameObject($"Mote_{i}");
            m.transform.SetParent(_motes, false);
            var mf = m.AddComponent<MeshFilter>();
            mf.sharedMesh = QuadMesh();
            var mr = m.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _moteMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            m.AddComponent<Mote>().Configure(baseRadius * 1.6f, height, i / 5f);
        }

        VFXAutoFade.HideFromMinimap(gameObject);
    }

    private void Update()
    {
        if (_dying) return;
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);
        float k = 1f + Mathf.Sin(Time.time * _beatRate) * pulse;
        SetTint(k);
        if (_light != null) _light.intensity = _lightBase * k;
    }

    // The seal has been lit and the vigil is running.
    //
    // The light stops being an invitation and becomes a warning: it spins up,
    // beats faster and burns brighter. That matters more than it sounds — the
    // player has just started a fight on a clock, and the object they are
    // defending should look like it is doing something, or the whole siege reads
    // as a HUD bar with scenery behind it.
    public void SetAgitated(bool on)
    {
        spinSpeed = on ? 48f : 9f;
        pulse = on ? 0.42f : 0.22f;
        _lightBase = on ? 2.8f : 1.6f;
        _beatRate = on ? 5.5f : 1.35f;
    }

    private float _beatRate = 1.35f;

    // Called when the chest opens. The beacon has to go out — a light still
    // burning over an emptied chest walks the player back to nothing.
    public void Extinguish()
    {
        if (_dying) return;
        _dying = true;
        StartCoroutine(FadeOut());
    }

    private IEnumerator FadeOut()
    {
        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(t / fadeTime);
            SetTint(k);
            if (_light != null) _light.intensity = _lightBase * k;
            // Collapses inward as it dies rather than only dimming, so it reads as
            // the light being spent instead of the effect being switched off.
            transform.localScale = new Vector3(Mathf.Lerp(0.2f, 1f, k), k, Mathf.Lerp(0.2f, 1f, k));
            yield return null;
        }
        Destroy(gameObject);
    }

    private void SetTint(float k)
    {
        if (_mat == null) return;
        Color c = colour * k;
        c.a = _baseAlpha * k;
        if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
        if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", c);
        if (_mat.HasProperty("_TintColor")) _mat.SetColor("_TintColor", c);
        if (_mat.HasProperty("_EmissionColor")) _mat.SetColor("_EmissionColor", c);
    }

    private void OnDestroy() { if (_mat != null) Destroy(_mat); }

    // ---- generated geometry and texture ---------------------------------------

    // An open cone, apex-down, with the UV's V running up it so the texture's
    // vertical fade puts the bright end on the ground where the chest is.
    private Mesh ShaftMesh()
    {
        const int segments = 14;
        var verts = new Vector3[(segments + 1) * 2];
        var uvs = new Vector2[verts.Length];
        var tris = new int[segments * 6];

        for (int i = 0; i <= segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            verts[i * 2] = dir * baseRadius;
            verts[i * 2 + 1] = dir * topRadius + Vector3.up * height;
            float u = i / (float)segments;
            uvs[i * 2] = new Vector2(u, 0f);
            uvs[i * 2 + 1] = new Vector2(u, 1f);
        }
        for (int i = 0; i < segments; i++)
        {
            int b = i * 2, t = i * 6;
            tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
            tris[t + 3] = b + 2; tris[t + 4] = b + 1; tris[t + 5] = b + 3;
        }

        var mesh = new Mesh { name = "BeaconShaft" };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh s_quad;
    private static Mesh QuadMesh()
    {
        if (s_quad != null) return s_quad;
        s_quad = new Mesh { name = "BeaconMote" };
        // Slightly larger than before, because the sprite now fades to nothing
        // well inside its own edge — the bright core is about the size the old
        // hard square was.
        const float h = 0.085f;
        s_quad.vertices = new[] { new Vector3(-h, -h, 0), new Vector3(h, -h, 0), new Vector3(-h, h, 0), new Vector3(h, h, 0) };
        // The WHOLE texture, so the falloff at its rim is what the quad's edge
        // shows. Sampling a window of a gradient is what made these rectangles.
        s_quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        s_quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        s_quad.RecalculateBounds();
        return s_quad;
    }

    // Bright at the bottom, gone at the top, soft at the vertical edges. Drawn
    // once and shared: the shaft's whole appearance is this gradient, so it is
    // worth the twenty lines and it costs no art dependency.
    private static Texture2D s_tex;
    private static Texture2D ShaftTexture()
    {
        if (s_tex != null) return s_tex;
        const int W = 64, H = 128;
        s_tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = y / (float)(H - 1);
            // Fades out towards the top, and eases off the very bottom too so the
            // shaft does not end in a hard bright ring on the ground.
            float vertical = Mathf.Pow(1f - v, 1.7f) * Mathf.Clamp01(v * 6f + 0.25f);
            for (int x = 0; x < W; x++)
            {
                float u = Mathf.Abs(x / (float)(W - 1) - 0.5f) * 2f;
                float across = Mathf.Pow(1f - u, 1.4f);
                px[y * W + x] = new Color(1f, 1f, 1f, vertical * across);
            }
        }
        s_tex.SetPixels(px);
        s_tex.Apply();
        return s_tex;
    }

    // A speck drifting up the shaft and starting again. Its own component so it
    // keeps moving independently of the beacon's rotation, and so it can face the
    // camera without the shaft having to.
    private class Mote : MonoBehaviour
    {
        private float _radius, _height, _phase, _speed;

        public void Configure(float radius, float height, float phase01)
        {
            _radius = radius;
            _height = height;
            _phase = phase01;
            _speed = Random.Range(0.22f, 0.4f);
        }

        private void Update()
        {
            _phase += Time.deltaTime * _speed;
            if (_phase > 1f) _phase -= 1f;

            float a = _phase * Mathf.PI * 4f;
            transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (_radius * (0.4f + _phase * 0.6f))
                                    + Vector3.up * (_phase * _height);

            var cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }
    }
}
