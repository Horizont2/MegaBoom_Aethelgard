using UnityEngine;

// Runtime rescue for prefabs whose material shader isn't in the build — a
// BiRP/Standard shader dragged into a URP project, or a shader stripped from
// the build — which Unity replaces with the pink Hidden/InternalErrorShader
// (the classic "everything is magenta" bug). Handles BOTH solid meshes and
// PARTICLE systems (main + trail materials), and — because a magenta particle
// on screen is worse than no particle for a trailer — disables any particle
// renderer it genuinely cannot re-home.
public static class ShaderRepair
{
    private const string ErrorShader = "Hidden/InternalErrorShader";

    private static Shader s_lit;
    private static Shader Lit => s_lit != null
        ? s_lit
        : (s_lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));

    private static bool s_particleResolved;
    private static Shader s_particle;
    private static Shader Particle
    {
        get
        {
            if (!s_particleResolved)
            {
                s_particle = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                          ?? Shader.Find("Universal Render Pipeline/Particles/Lit")
                          ?? Shader.Find("Sprites/Default")
                          ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                s_particleResolved = true;
            }
            return s_particle;
        }
    }

    private static bool IsBroken(Material m) =>
        m == null || m.shader == null || m.shader.name == ErrorShader;

    public static void Fix(GameObject root)
    {
        if (root == null) return;
        var rends = root.GetComponentsInChildren<Renderer>(true);
        for (int ri = 0; ri < rends.Length; ri++)
        {
            var r = rends[ri];
            if (r == null) continue;

            bool isParticle = r is ParticleSystemRenderer;
            Shader target = isParticle ? Particle : Lit;

            // Main material slot(s).
            var mats = r.materials;   // instanced copies — safe to reassign
            bool changed = false;
            bool unfixable = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!IsBroken(mats[i])) continue;
                if (target == null) { unfixable = true; continue; }
                mats[i] = Rehome(mats[i], target);
                changed = true;
            }
            if (changed) r.materials = mats;

            // Particle systems have a SEPARATE trail material slot that
            // r.materials never touches — a magenta trail slipped through before.
            if (isParticle)
            {
                var psr = (ParticleSystemRenderer)r;
                if (IsBroken(psr.trailMaterial))
                {
                    if (target != null) psr.trailMaterial = Rehome(psr.trailMaterial, target);
                    else unfixable = true;
                }
            }

            // Last resort: a magenta particle we can't re-home is DISABLED so it
            // never shows purple on screen (a trailer must be clean).
            if (unfixable && isParticle) r.enabled = false;
        }
    }

    // Scene-wide safety net: repair any magenta PARTICLE renderer currently in
    // the scene, no matter which prefab spawned it. Cheap — only touches renderers
    // that are actually broken; once repaired they're skipped forever.
    public static void SweepParticles()
    {
        var psrs = Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < psrs.Length; i++)
        {
            var r = psrs[i];
            if (r == null) continue;
            bool broken = IsBroken(r.sharedMaterial) || IsBroken(r.trailMaterial);
            if (broken) Fix(r.gameObject);
        }
    }

    private static Material Rehome(Material broken, Shader target)
    {
        Color c = (broken != null && broken.HasProperty("_Color")) ? broken.color : new Color(0.78f, 0.75f, 0.68f);
        Texture tex = (broken != null && broken.HasProperty("_MainTex")) ? broken.mainTexture : null;

        var nm = new Material(target);
        if (nm.HasProperty("_BaseColor")) nm.SetColor("_BaseColor", c); else if (nm.HasProperty("_Color")) nm.SetColor("_Color", c);
        if (tex != null)
        {
            if (nm.HasProperty("_BaseMap")) nm.SetTexture("_BaseMap", tex);
            else if (nm.HasProperty("_MainTex")) nm.SetTexture("_MainTex", tex);
        }
        return nm;
    }
}
