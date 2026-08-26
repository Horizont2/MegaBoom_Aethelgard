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

        // Measure the BODY only. Prefer the skinned body mesh so a held staff /
        // weapon (a separate MeshRenderer that juts out sideways) doesn't skew
        // the centre or inflate the size — that's what made the burst sit
        // crooked and too big next to the mage/necromancer.
        Bounds b = new Bounds(source.position, Vector3.zero);
        bool any = false;
        var skinned = source.GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var r in skinned)
        {
            if (r == null) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any)
        {
            foreach (var r in source.GetComponentsInChildren<MeshRenderer>())
            {
                if (r == null) continue;
                string n = r.name.ToLowerInvariant();
                if (n.Contains("staff") || n.Contains("weapon") || n.Contains("sword") ||
                    n.Contains("bow") || n.Contains("wand")) continue;   // skip held props
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
        }

        float bodyHeight = any ? Mathf.Clamp(b.size.y, 0.6f, 4f) : 1.7f;
        float radius = any ? Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 0.35f, 0.15f, 0.5f) : 0.3f;
        // Anchor at the WAIST: horizontally on the body pivot (not the staff-
        // skewed bounds centre), vertically halfway up from the feet.
        float feetY = any ? b.min.y : source.position.y;
        Vector3 center = new Vector3(source.position.x, feetY + bodyHeight * 0.5f, source.position.z);

        // Emit over a shorter volume centred on the waist so it hugs the torso.
        float emitHeight = bodyHeight * 0.55f;

        var root = new GameObject("DeathAsh");
        root.transform.position = center;

        // (Bone shards are now REAL mesh chunks via SkeletonShatter — this stays
        // as the accompanying dust + embers puff.)
        BuildAsh(root.transform, emitHeight, radius);
        BuildEmbers(root.transform, emitHeight, radius);
        BuildDust(root.transform, radius);
        VFXAutoFade.HideFromMinimap(root);   // keep ash off the minimap

        // Auto-clean after the longest layer finishes (lifetime + emission window).
        Object.Destroy(root, 5f);
    }

    // ── Bone shards — chunky fragments that burst from the whole body and fall,
    //    so the skeleton reads as breaking into pieces. ──
    private static void BuildBoneShards(Transform parent, float bodyHeight, float radius)
    {
        var go = new GameObject("BoneShards");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        var main = ps.main;
        main.duration = 0.4f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 3.8f);   // burst outward
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);  // chunky, bigger than ash
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.86f, 0.83f, 0.72f), new Color(0.66f, 0.63f, 0.55f));  // bone / weathered bone
        main.gravityModifier = 1.0f;   // they fall — key to "breaking apart"
        main.maxParticles = 80;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 34) });   // one shatter burst

        // Emit over the WHOLE body so fragments look like the whole skeleton.
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(radius * 1.4f, Mathf.Max(0.6f, bodyHeight), radius * 1.4f);

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);   // tumble

        // Bounce off the ground and settle — sells the physical shatter.
        var col = ps.collision;
        col.enabled = true;
        col.type = ParticleSystemCollisionType.World;
        col.mode = ParticleSystemCollisionMode.Collision3D;
        col.bounce = 0.35f;
        col.dampen = 0.3f;
        col.quality = ParticleSystemCollisionQuality.Medium;

        var colOL = ps.colorOverLifetime;
        colOL.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(0.86f, 0.83f, 0.72f), 0f), new GradientColorKey(new Color(0.6f, 0.57f, 0.5f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        colOL.color = g;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0.3f)));

        ApplyRenderer(ps, additive: false, new Color(0.82f, 0.79f, 0.68f, 1f));
        ps.Play();
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
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.55f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.10f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.62f, 0.60f, 0.57f), new Color(0.42f, 0.40f, 0.38f));
        main.gravityModifier = -0.03f;              // drift upward
        main.maxParticles = 350;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission;
        em.rateOverTime = 90f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 45) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(radius * 1.2f, height, radius * 1.2f);

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
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
        // HDR-bright orange so the URP bloom makes them actually glow.
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(2.4f, 1.1f, 0.35f), new Color(1.6f, 0.6f, 0.15f));
        main.gravityModifier = -0.05f;
        main.maxParticles = 90;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission;
        em.rateOverTime = 18f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(radius * 1.1f, height * 0.9f, radius * 1.1f);

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
        go.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        var main = ps.main;
        main.duration = 0.8f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
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
