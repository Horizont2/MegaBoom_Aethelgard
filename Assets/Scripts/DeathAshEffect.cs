using UnityEngine;

// Procedural "crumble to ash" death effect for enemy skeletons — no art asset
// needed. Emits over the dead body's silhouette and disperses on the wind:
//   • fine ASH motes — many soft grey flakes that rise, swirl on turbulence,
//     tumble, and fade as they scatter,
//   • glowing EMBERS — a few hot orange sparks (additive, so bloom makes them
//     glow) that flare then die to red,
//   • a soft DUST wisp at the base that swells and fades to ground the effect.
// Call DeathAshEffect.Spawn(enemyTransform) the moment the body starts to
// dissolve. The instance auto-destroys once every particle has finished.
public static class DeathAshEffect
{
    public static void Spawn(Transform source)
    {
        if (source == null) return;

        // Fit the emission volume to the body's rendered bounds so tall/short
        // enemies both look right; fall back to a humanoid default.
        Vector3 center = source.position + Vector3.up * 0.9f;
        float height = 1.8f, radius = 0.4f;
        var rends = source.GetComponentsInChildren<Renderer>();
        bool any = false;
        Bounds b = new Bounds(source.position, Vector3.zero);
        foreach (var r in rends)
        {
            if (r == null || r is TrailRenderer || r is LineRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (any)
        {
            center = b.center;
            height = Mathf.Clamp(b.size.y, 0.6f, 4f);
            radius = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 0.5f, 0.2f, 1.5f);
        }

        var root = new GameObject("DeathAsh");
        root.transform.position = center;

        BuildAsh(root.transform, height, radius);
        BuildEmbers(root.transform, height, radius);
        BuildDust(root.transform, radius);

        // Auto-clean after the longest layer finishes (lifetime + emission window).
        Object.Destroy(root, 5f);
    }

    // ── Fine ash flakes — the body of the effect ──
    private static void BuildAsh(Transform parent, float height, float radius)
    {
        var go = new GameObject("Ash");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        var main = ps.main;
        main.duration = 1.3f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.14f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.62f, 0.60f, 0.57f), new Color(0.42f, 0.40f, 0.38f));
        main.gravityModifier = -0.03f;              // drift upward
        main.maxParticles = 500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission;
        em.rateOverTime = 130f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 70) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(radius * 1.7f, height, radius * 1.7f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.y = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);   // rise
        vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.35f);  // wind sway
        vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.4f;
        noise.frequency = 0.35f;
        noise.scrollSpeed = 0.35f;
        noise.quality = ParticleSystemNoiseQuality.High;
        noise.damping = true;

        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.12f;
        limit.limit = new ParticleSystem.MinMaxCurve(1.6f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] {
                new GradientColorKey(new Color(0.7f, 0.68f, 0.64f), 0f),
                new GradientColorKey(new Color(0.5f, 0.49f, 0.47f), 1f)
            },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.85f, 0.15f),
                new GradientAlphaKey(0.6f, 0.6f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = g;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.15f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-1.6f, 1.6f);

        ApplyRenderer(ps, additive: false, new Color(0.6f, 0.58f, 0.55f, 1f));
        ps.Play();
    }

    // ── Glowing embers — the "expensive" hot sparks ──
    private static void BuildEmbers(Transform parent, float height, float radius)
    {
        var go = new GameObject("Embers");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        var main = ps.main;
        main.duration = 1.0f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 2.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        // HDR-bright orange so the URP bloom makes them actually glow.
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(2.4f, 1.1f, 0.35f), new Color(1.6f, 0.6f, 0.15f));
        main.gravityModifier = -0.05f;
        main.maxParticles = 120;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission;
        em.rateOverTime = 26f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 20) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(radius * 1.4f, height * 0.85f, radius * 1.4f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.y = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.5f;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 0.75f, 0.35f), 0f),
                new GradientColorKey(new Color(1f, 0.35f, 0.08f), 0.55f),
                new GradientColorKey(new Color(0.35f, 0.08f, 0.02f), 1f)
            },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(0.9f, 0.6f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = g;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

        ApplyRenderer(ps, additive: true, new Color(1f, 0.6f, 0.2f, 1f));
        ps.Play();
    }

    // ── Base dust wisp — grounds the effect ──
    private static void BuildDust(Transform parent, float radius)
    {
        var go = new GameObject("Dust");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, -0.7f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        var main = ps.main;
        main.duration = 0.8f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.5f, 0.48f, 0.45f, 0.5f), new Color(0.4f, 0.38f, 0.36f, 0.4f));
        main.gravityModifier = -0.01f;
        main.maxParticles = 30;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = Mathf.Max(0.2f, radius);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(0.5f, 0.48f, 0.45f), 0f),
                    new GradientColorKey(new Color(0.42f, 0.4f, 0.38f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.4f, 0.25f),
                    new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 1.3f)));

        ApplyRenderer(ps, additive: false, new Color(0.5f, 0.48f, 0.45f, 1f));
        ps.Play();
    }

    private static Shader s_unlit;

    private static void ApplyRenderer(ParticleSystem ps, bool additive, Color matColor)
    {
        var rend = ps.GetComponent<ParticleSystemRenderer>();
        if (rend == null) return;

        if (s_unlit == null)
            s_unlit = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Sprites/Default");

        var mat = new Material(s_unlit);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", matColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", matColor);

        // Additive blend for the embers so bloom makes them glow; soft alpha
        // for ash/dust. Guarded — property names vary across shader versions.
        if (additive)
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);   // transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f);       // additive preset
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        rend.material = mat;
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.sortMode = ParticleSystemSortMode.Distance;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
    }
}
