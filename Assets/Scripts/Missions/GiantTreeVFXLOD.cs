using UnityEngine;
using System.Collections.Generic;

// Auto-attached LOD throttle for the giant-tree leaf/snow VFX + the
// heavy trunk / canopy renderers on the parent tree.
//
// History:
// - v1 disabled shadow-casting.
// - v2 killed particle rate at far distance and disabled lights past
//   ~12m. Still froze the frame when the player walked between two or
//   three giant trees in the same forest cell.
// - v3 (this pass) fixes three separate contributors that ALL fire
//   on approach and none of which the previous version solved:
//     * Per-tick GC — SetParticles was calling GetComponentsInChildren
//       every 0.25 s on every tree; that's an allocation drip that
//       forces regular GC pauses right where the player expects
//       smoothness.
//     * Missing global light cap — Forward+ URP tile-list rebuilds
//       stall the render thread when many small point lights bunch
//       up. We now enforce "only the N nearest giant trees may have
//       their lights on at once" globally.
//     * Tree renderer never culled — the trunk + canopy meshes were
//       shadow-off'd but always rendered. Now they hard-toggle past
//       an extreme cull distance with hysteresis to avoid popping.
//
// If a freeze persists after this, the remaining suspects are prefab-
// side and OUT of code's reach:
//   • MeshCollider on the trunk — replace with a Capsule.
//   • No LODGroup — add one with a 60 % / 30 % / 15 % triangle chain.
//   • Transparent alpha-blend leaves — try alpha-tested (Cutout) for
//     the far LOD.
//   • Shader variant compile hitch on first-approach — add the giant
//     tree shaders to Project Settings → Graphics → Preloaded Shaders.
public class GiantTreeVFXLOD : MonoBehaviour
{
    [Tooltip("Particles run at full multiplier when player is closer than this.")]
    public float nearDistance = 15f;
    [Tooltip("Particles + renderers off entirely beyond this distance.")]
    public float farDistance = 45f;
    [Tooltip("Per-light pixel-shading cap. Lights disabled past this distance from the player.")]
    public float lightDistance = 12f;
    [Tooltip("Extreme cull — tree trunk/canopy renderers hide past this distance. Give it plenty of headroom over farDistance to hide the pop.")]
    public float treeCullDistance = 90f;
    [Tooltip("Hard cap on simultaneous particles.")]
    public int maxParticlesCap = 12;
    [Tooltip("Cap on the close-range emission multiplier (1.0 = original prefab rate).")]
    [Range(0.05f, 1f)] public float closeRangeMultiplierCap = 0.3f;
    [Tooltip("Global cap on giant-tree lights active at once — only the N nearest instances get their lights on. Forward+ URP tile rebuilds stall past ~4 close lights.")]
    public static int GlobalActiveLightCap = 3;

    private struct PSCache
    {
        public ParticleSystem ps;
        public ParticleSystemRenderer renderer;
        public float baseRate;
        public Collider[] cachedColliders;
    }

    private PSCache[] systems;
    private Light[] lights;
    private float[] lightBaseIntensities;
    private Renderer[] treeRenderers;
    private LODGroup treeLodGroup;
    private Transform player;
    private float nextCheckTime;
    private float lastPlayerSqrDist = float.MaxValue;
    private const float CHECK_INTERVAL = 0.25f;
    private bool particlesOn = true;
    private bool lightsOn = false; // resolved by first Update tick
    private bool treesVisible = true;

    // Global registry so we can enforce the light cap.
    private static readonly List<GiantTreeVFXLOD> s_registry = new List<GiantTreeVFXLOD>(64);
    private static float s_nextGlobalSort;
    private const float GLOBAL_SORT_INTERVAL = 0.4f;

    private void OnEnable() { if (!s_registry.Contains(this)) s_registry.Add(this); }
    private void OnDisable() { s_registry.Remove(this); }

    private void Awake()
    {
        // --- particles + their child colliders (cached ONCE — the old
        //     code allocated a Collider[] every 0.25 s per tree). ---
        ParticleSystem[] all = GetComponentsInChildren<ParticleSystem>(true);
        systems = new PSCache[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            ParticleSystem ps = all[i];
            var em = ps.emission;
            systems[i].ps = ps;
            systems[i].baseRate = em.rateOverTime.constant;
            systems[i].renderer = ps.GetComponent<ParticleSystemRenderer>();
            systems[i].cachedColliders = ps.GetComponentsInChildren<Collider>(true);

            var main = ps.main;
            if (main.maxParticles > maxParticlesCap) main.maxParticles = maxParticlesCap;
            if (systems[i].renderer != null)
            {
                systems[i].renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                systems[i].renderer.receiveShadows = false;
                systems[i].renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
        }

        // --- lights on this subtree + the parent tree subtree ---
        Light[] selfLights = GetComponentsInChildren<Light>(true);
        Light[] parentLights = transform.parent != null
            ? transform.parent.GetComponentsInChildren<Light>(true)
            : System.Array.Empty<Light>();
        List<Light> merged = new List<Light>(selfLights);
        for (int i = 0; i < parentLights.Length; i++)
            if (!merged.Contains(parentLights[i])) merged.Add(parentLights[i]);
        lights = merged.ToArray();
        lightBaseIntensities = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
        {
            Light l = lights[i];
            if (l == null) continue;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.Auto;
            if (l.intensity > 1.2f) l.intensity = 1.2f; // prefab rates were 8-15
            lightBaseIntensities[i] = l.intensity;
            l.enabled = false; // start off — global sort turns nearest ones on
        }

        // --- parent tree mesh renderers: shadow-off + no motion vectors,
        //     cached for the far-cull toggle. ---
        if (transform.parent != null)
        {
            treeLodGroup = transform.parent.GetComponent<LODGroup>();
            Renderer[] r = transform.parent.GetComponentsInChildren<Renderer>(true);
            var keep = new List<Renderer>(r.Length);
            for (int i = 0; i < r.Length; i++)
            {
                if (r[i] == null) continue;
                if (r[i] is ParticleSystemRenderer) continue;
                r[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r[i].motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                keep.Add(r[i]);
            }
            treeRenderers = keep.ToArray();
        }

        nextCheckTime = Time.unscaledTime + Random.Range(0f, CHECK_INTERVAL);
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCheckTime) return;
        nextCheckTime = Time.unscaledTime + CHECK_INTERVAL;

        if (player == null) player = CameraCache.MainTransform;
        if (player == null) return;

        float sqr = (player.position - transform.position).sqrMagnitude;
        lastPlayerSqrDist = sqr;
        float farSqr = farDistance * farDistance;
        float nearSqr = nearDistance * nearDistance;
        float cullSqr = treeCullDistance * treeCullDistance;

        // === Particles ===
        if (sqr >= farSqr)
        {
            if (particlesOn) SetParticles(false, 0f);
        }
        else
        {
            float t = Mathf.Clamp01(1f - (sqr - nearSqr) / Mathf.Max(0.001f, farSqr - nearSqr));
            t = Mathf.Min(t, closeRangeMultiplierCap);
            SetParticles(true, t);
        }

        // === Tree renderer hard-cull with 10 % hysteresis so a player
        //     hovering at the boundary doesn't pop the mesh in and out. ===
        bool shouldShow = treesVisible
            ? sqr < cullSqr * 1.10f
            : sqr < cullSqr;
        if (shouldShow != treesVisible) SetTreeRenderers(shouldShow);

        // === Lights: the global sorter picks the N nearest active
        //     instances; this instance just decides "should I be
        //     within range at all?" and defers the actual on/off to
        //     the sorter. ===
        if (Time.unscaledTime >= s_nextGlobalSort)
        {
            s_nextGlobalSort = Time.unscaledTime + GLOBAL_SORT_INTERVAL;
            ResolveGlobalLightCap();
        }
    }

    // Runs on one instance's Update per sort interval — walks the
    // registry, sorts by squared distance, enables the N nearest
    // within lightDistance and disables everyone else.
    private static void ResolveGlobalLightCap()
    {
        int cap = Mathf.Max(0, GlobalActiveLightCap);
        // Fast pass: mark everyone off first.
        for (int i = 0; i < s_registry.Count; i++)
        {
            var t = s_registry[i];
            if (t == null || t.lights == null) continue;
            if (t.lastPlayerSqrDist > t.lightDistance * t.lightDistance)
            {
                if (t.lightsOn) t.SetLights(false);
            }
        }
        if (cap == 0) return;

        // Now pick the top-N nearest that are still in range.
        // Small N (usually 3) so an insertion sort is fine.
        System.Span<int> topIdx = stackalloc int[16];
        System.Span<float> topSqr = stackalloc float[16];
        int topN = Mathf.Min(cap, topIdx.Length);
        int filled = 0;
        for (int i = 0; i < s_registry.Count; i++)
        {
            var t = s_registry[i];
            if (t == null || t.lights == null || t.lights.Length == 0) continue;
            float sq = t.lastPlayerSqrDist;
            if (sq > t.lightDistance * t.lightDistance) continue;

            // Insert into the sorted top-N (ascending by distance).
            int insertAt = filled;
            for (int k = 0; k < filled; k++) if (sq < topSqr[k]) { insertAt = k; break; }
            if (insertAt >= topN) continue;
            int end = Mathf.Min(filled, topN - 1);
            for (int k = end; k > insertAt; k--) { topSqr[k] = topSqr[k - 1]; topIdx[k] = topIdx[k - 1]; }
            topSqr[insertAt] = sq;
            topIdx[insertAt] = i;
            if (filled < topN) filled++;
        }
        for (int k = 0; k < filled; k++)
        {
            var t = s_registry[topIdx[k]];
            if (t != null && !t.lightsOn) t.SetLights(true);
        }
    }

    private void SetParticles(bool on, float multiplier)
    {
        if (systems == null) return;
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem ps = systems[i].ps;
            if (ps == null) continue;
            var em = ps.emission;
            em.enabled = on;
            if (on)
            {
                em.rateOverTime = systems[i].baseRate * multiplier;
                if (systems[i].renderer != null && !systems[i].renderer.enabled) systems[i].renderer.enabled = true;
            }
            else
            {
                ps.Clear();
                if (systems[i].renderer != null) systems[i].renderer.enabled = false;
            }

            // Use the cached collider list — no more per-tick GC.
            var cols = systems[i].cachedColliders;
            if (cols != null)
            {
                for (int c = 0; c < cols.Length; c++)
                    if (cols[c] != null) cols[c].enabled = on;
            }
        }
        particlesOn = on;
    }

    private void SetLights(bool on)
    {
        if (lights == null) return;
        for (int i = 0; i < lights.Length; i++)
        {
            Light l = lights[i];
            if (l == null) continue;
            l.enabled = on;
            if (on) l.intensity = lightBaseIntensities[i];
        }
        lightsOn = on;
    }

    private void SetTreeRenderers(bool on)
    {
        treesVisible = on;

        // When a LODGroup is present, drive IT rather than the individual
        // renderers. The old code force-enabled ALL cached renderers at once,
        // which meant every LOD mesh (LOD0..LOD3) drew simultaneously until the
        // next LOD boundary — quadrupling the draw + overdraw. Toggling the
        // LODGroup lets Unity keep exactly one LOD active; on show it re-selects
        // the correct single LOD, on hide we also disable the meshes so nothing
        // lingers at the last-picked level.
        if (treeLodGroup != null)
        {
            if (on)
            {
                treeLodGroup.enabled = true;   // re-selects the one correct LOD
            }
            else
            {
                treeLodGroup.enabled = false;
                if (treeRenderers != null)
                    for (int i = 0; i < treeRenderers.Length; i++)
                        if (treeRenderers[i] != null) treeRenderers[i].enabled = false;
            }
            return;
        }

        if (treeRenderers == null) return;
        for (int i = 0; i < treeRenderers.Length; i++)
            if (treeRenderers[i] != null) treeRenderers[i].enabled = on;
    }
}
