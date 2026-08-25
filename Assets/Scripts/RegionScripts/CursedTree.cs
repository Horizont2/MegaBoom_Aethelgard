using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A blighted (dead) tree that stands in a cursed story region and TRANSFORMS into
// a living, blooming tree when the region is purified. The victory flythrough
// drives an expanding purification wave from the totem (see BeginWorldBloom); as
// the wave reaches each cursed tree it blooms — the dead husk sinks away while a
// fresh tree unfurls with an overshoot pop, a green life-flash and a petal/light
// burst. Ties the "curse lifting" beat directly to the camera sweeping the land.
public class CursedTree : MonoBehaviour
{
    [Tooltip("The living tree this husk becomes when the curse lifts. Assigned at spawn by WorldGenerator.")]
    public GameObject bloomedPrefab;
    [Tooltip("Optional burst VFX at the moment of bloom. A procedural light/petal puff is used if empty.")]
    public GameObject bloomBurstVFX;

    private Material biomeMaterial;   // per-biome trunk/foliage material to paint the bloomed tree
    private Color foliageColor = Color.white;
    private bool useMaterial;
    private bool useColor;
    private bool bloomed;

    // Registry so the victory routine can bloom the whole forest without a scene scan.
    public static readonly List<CursedTree> Active = new List<CursedTree>(256);

    // Expanding purification wave, shared by all cursed trees.
    private static bool s_waveActive;
    private static Vector3 s_waveCenter;
    private static float s_waveStartTime;
    private static float s_waveDuration = 8f;
    private static float s_waveMaxRadius = 300f;

    private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
    private void OnDisable() { Active.Remove(this); }

    // Called by WorldGenerator right after spawning the husk.
    public void Configure(GameObject bloomed, Material mat, Color foliage, bool applyMat, bool applyColor, GameObject burstVfx)
    {
        bloomedPrefab = bloomed;
        biomeMaterial = mat;
        foliageColor = foliage;
        useMaterial = applyMat && mat != null;
        useColor = applyColor;
        if (burstVfx != null) bloomBurstVFX = burstVfx;
    }

    // Kick off the outward-sweeping bloom. Call once at the start of the victory
    // flight; trees bloom as the wave radius passes them, in sync with the camera.
    public static void BeginWorldBloom(Vector3 center, float duration, float maxRadius)
    {
        s_waveActive = true;
        s_waveCenter = center;
        s_waveStartTime = Time.time;
        s_waveDuration = Mathf.Max(0.5f, duration);
        s_waveMaxRadius = Mathf.Max(10f, maxRadius);
    }

    // Reset between regions so a new region's trees don't insta-bloom.
    public static void ResetWave() { s_waveActive = false; }

    private void Update()
    {
        if (bloomed || !s_waveActive) return;
        float t = Mathf.Clamp01((Time.time - s_waveStartTime) / s_waveDuration);
        float radius = t * s_waveMaxRadius;
        Vector3 d = transform.position - s_waveCenter; d.y = 0f;
        if (d.sqrMagnitude <= radius * radius)
            StartCoroutine(BloomRoutine());
    }

    private IEnumerator BloomRoutine()
    {
        bloomed = true;

        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;
        Vector3 scale = transform.localScale;

        // Burst of life at the base.
        SpawnBurst(pos + Vector3.up * 1.5f);

        // Spawn the living tree, painted to the biome, starting small.
        GameObject alive = null;
        if (bloomedPrefab != null)
        {
            alive = Instantiate(bloomedPrefab, pos, rot, transform.parent);
            alive.transform.localScale = scale * 0.35f;
            PaintBiome(alive);
            StartCoroutine(GrowRoutine(alive.transform, scale, 0.9f));
            StartCoroutine(LifeFlashRoutine(alive));
        }

        // The dead husk sinks + shrinks away, then is removed.
        float t = 0f; const float dur = 0.7f;
        Vector3 startS = scale;
        Vector3 startP = pos;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = t / dur;
            transform.localScale = Vector3.Lerp(startS, new Vector3(startS.x, startS.y * 0.15f, startS.z), k);
            transform.position = startP + Vector3.down * (k * 2.5f);
            yield return null;
        }
        Destroy(gameObject);
    }

    private static IEnumerator GrowRoutine(Transform t, Vector3 target, float dur)
    {
        if (t == null) yield break;
        Vector3 from = t.localScale;
        float e = 0f;
        while (e < dur)
        {
            if (t == null) yield break;
            e += Time.deltaTime;
            float k = Mathf.Clamp01(e / dur);
            // Ease-out with a slight overshoot so the tree "springs" to life.
            float s = 1f - Mathf.Pow(1f - k, 3f);
            float overshoot = Mathf.Sin(k * Mathf.PI) * 0.08f;
            t.localScale = Vector3.LerpUnclamped(from, target, s) * (1f + overshoot);
            yield return null;
        }
        if (t != null) t.localScale = target;
    }

    // Brief green emissive flash on the new tree that settles to normal.
    private static IEnumerator LifeFlashRoutine(GameObject go)
    {
        if (go == null) yield break;
        Renderer[] rends = go.GetComponentsInChildren<Renderer>();
        var mats = new List<Material>();
        foreach (var r in rends) if (r != null) mats.Add(r.material);
        foreach (var m in mats) { if (m != null) { m.EnableKeyword("_EMISSION"); } }
        float t = 0f; const float dur = 0.9f;
        Color flash = new Color(0.2f, 0.9f, 0.35f);
        while (t < dur)
        {
            t += Time.deltaTime;
            float a = Mathf.Sin(Mathf.Clamp01(t / dur) * Mathf.PI); // up then down
            foreach (var m in mats) if (m != null) m.SetColor("_EmissionColor", flash * a * 1.2f);
            yield return null;
        }
        foreach (var m in mats) if (m != null) m.SetColor("_EmissionColor", Color.black);
    }

    private void PaintBiome(GameObject go)
    {
        if (go == null) return;
        Renderer[] rends = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends)
        {
            if (r == null) continue;
            if (useMaterial) r.sharedMaterial = biomeMaterial;
            else if (useColor)
            {
                var mat = r.material;
                if (mat != null) mat.color = foliageColor;
            }
        }
    }

    private void SpawnBurst(Vector3 at)
    {
        if (bloomBurstVFX != null) { Instantiate(bloomBurstVFX, at, Quaternion.identity); return; }

        // Procedural golden/green life puff — no asset needed.
        var go = new GameObject("BloomBurst");
        go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();
        var main = ps.main;
        main.duration = 1.2f; main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        main.startColor = new Color(0.7f, 1f, 0.5f, 1f);
        main.gravityModifier = -0.02f;
        main.maxParticles = 60;
        var em = ps.emission; em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.6f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(0.8f, 1f, 0.55f), 0f), new GradientColorKey(new Color(1f, 0.9f, 0.5f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
        rend.material.color = new Color(0.8f, 1f, 0.55f, 1f);
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        Destroy(go, 2f);
    }
}
