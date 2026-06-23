using UnityEngine;
using System.Collections.Generic;

// Auto-attached LOD throttle for the giant-tree leaf/snow VFX.
//
// The previous version capped emission rate and shadow casting which
// still left the GPU rendering tens of overlapping transparent quads
// plus per-light cost the moment the camera rotated toward a tree.
// On the user's hardware that dropped FPS to 10-20 when standing
// next to a giant tree and panning the camera.
//
// This pass is far more aggressive:
//   * Past farDistance, ParticleSystemRenderer.enabled = false
//     (zero draw cost, no overdraw).
//   * Past lightDistance, Light.enabled = false — lights are the
//     single biggest hidden cost; killing them when the player is
//     more than ~12m away from the tree is invisible to the eye but
//     huge for the per-tile light list in Forward+.
//   * Particle emission still scales smoothly inside nearDistance
//     but is hard-capped at 0.3 multiplier, and maxParticles is
//     clamped to 12 (was 24).
//   * When the tree leaves the active range we Clear() the
//     particle system so there's no residual overdraw from dying
//     particles.
//   * The MeshRenderer / SkinnedMeshRenderer on the tree is forced
//     to ShadowCastingMode.Off — saves a shadow pass per tree.
public class GiantTreeVFXLOD : MonoBehaviour
{
    [Tooltip("Particles run at full multiplier when player is closer than this.")]
    public float nearDistance = 15f;
    [Tooltip("Particles + renderers off entirely beyond this distance.")]
    public float farDistance = 45f;
    [Tooltip("Per-light pixel-shading cap. Lights are disabled past this distance from the player.")]
    public float lightDistance = 12f;
    [Tooltip("Hard cap on simultaneous particles.")]
    public int maxParticlesCap = 12;
    [Tooltip("Cap on the close-range emission multiplier (1.0 = original prefab rate). Lower values are harder on the eyes vs. lower GPU cost.")]
    [Range(0.05f, 1f)] public float closeRangeMultiplierCap = 0.3f;

    private struct PSCache
    {
        public ParticleSystem ps;
        public ParticleSystemRenderer renderer;
        public float baseRate;
    }

    private PSCache[] systems;
    private Light[] lights;
    private float[] lightBaseIntensities;
    private Renderer[] treeRenderers; // mesh renderers on the parent tree
    private Transform player;
    private float nextCheckTime;
    private const float CHECK_INTERVAL = 0.25f;
    private bool particlesOn = true;
    private bool lightsOn = true;

    private void Awake()
    {
        // --- particles ---
        ParticleSystem[] all = GetComponentsInChildren<ParticleSystem>(true);
        systems = new PSCache[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            ParticleSystem ps = all[i];
            var em = ps.emission;
            systems[i].ps = ps;
            systems[i].baseRate = em.rateOverTime.constant;
            systems[i].renderer = ps.GetComponent<ParticleSystemRenderer>();

            var main = ps.main;
            if (main.maxParticles > maxParticlesCap) main.maxParticles = maxParticlesCap;
            if (systems[i].renderer != null)
            {
                systems[i].renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                systems[i].renderer.receiveShadows = false;
            }
        }

        // --- lights (this subtree + parent tree subtree) ---
        Light[] selfLights = GetComponentsInChildren<Light>(true);
        Light[] parentLights = transform.parent != null
            ? transform.parent.GetComponentsInChildren<Light>(true)
            : new Light[0];
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
            if (l.intensity > 1.2f) l.intensity = 1.2f; // hard clamp — prefab rates were 8-15
            lightBaseIntensities[i] = l.intensity;
        }

        // --- parent tree mesh shadow casting off ---
        if (transform.parent != null)
        {
            Renderer[] r = transform.parent.GetComponentsInChildren<Renderer>(true);
            treeRenderers = r;
            for (int i = 0; i < r.Length; i++)
            {
                if (r[i] == null) continue;
                // Skip ourselves to keep particles configurable through the cache above
                if (r[i] is ParticleSystemRenderer) continue;
                r[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
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
        float farSqr = farDistance * farDistance;
        float nearSqr = nearDistance * nearDistance;
        float lightSqr = lightDistance * lightDistance;

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

        // === Lights ===
        bool lightsShouldBeOn = sqr <= lightSqr;
        if (lightsShouldBeOn != lightsOn) SetLights(lightsShouldBeOn);
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
                // Hard stop: kill existing particles so we don't pay
                // overdraw cost for the last second of fading particles.
                ps.Clear();
                if (systems[i].renderer != null) systems[i].renderer.enabled = false;
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
}
