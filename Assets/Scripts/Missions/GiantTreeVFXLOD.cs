using UnityEngine;

// Auto-attached LOD throttle for the giant-tree leaf/snow VFX. The
// instantiated effect prefab is a dense ParticleSystem set that runs
// continuously even when the player isn't looking at it; multiplied
// across every giant tree in a combat region it dominates the GPU
// budget, and walking right up to one stalled the frame.
//
// This component:
//   - Stops emission entirely when the player is past `farDistance`.
//   - Scales emission rate smoothly across `nearDistance..farDistance`.
//   - Hard-caps maxParticles so even the close case can't unbounded-spawn.
//   - Disables shadow casting on the renderer.
//   - Polls cheaply (5 Hz, with a per-instance phase offset).
public class GiantTreeVFXLOD : MonoBehaviour
{
    public float nearDistance = 22f;
    public float farDistance = 55f;
    public int maxParticlesCap = 24;

    private struct PSCache
    {
        public ParticleSystem ps;
        public float baseRate;
    }

    private PSCache[] systems;
    private Light[] lights;
    private float[] lightBaseIntensities;
    private Transform player;
    private float nextCheckTime;
    private float checkInterval = 0.2f;
    private bool emittingNow = true;

    private void Awake()
    {
        ParticleSystem[] all = GetComponentsInChildren<ParticleSystem>(true);
        systems = new PSCache[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            ParticleSystem ps = all[i];
            var em = ps.emission;
            systems[i].ps = ps;
            systems[i].baseRate = em.rateOverTime.constant;

            // Hard particle cap — close-up VFX could otherwise emit unbounded.
            var main = ps.main;
            if (main.maxParticles > maxParticlesCap) main.maxParticles = maxParticlesCap;
            ParticleSystemRenderer rend = ps.GetComponent<ParticleSystemRenderer>();
            if (rend != null)
            {
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }
        }

        // Lights buried inside the VFX prefab (and on the parent giant
        // tree itself) are the silent FPS killer — every tree spawned
        // its own point light with shadows, and a forest of them dragged
        // the GPU into single-digit frame rates the moment the camera
        // saw several at once. Walk BOTH this VFX and the tree it's
        // parented to. Strip shadows unconditionally and cache intensity
        // so we can fade lights with distance like the particles.
        Light[] selfLights = GetComponentsInChildren<Light>(true);
        Light[] parentLights = transform.parent != null
            ? transform.parent.GetComponentsInChildren<Light>(true)
            : new Light[0];
        // Merge, deduping (parentLights includes our own subtree).
        System.Collections.Generic.List<Light> merged = new System.Collections.Generic.List<Light>(selfLights);
        for (int i = 0; i < parentLights.Length; i++)
        {
            if (!merged.Contains(parentLights[i])) merged.Add(parentLights[i]);
        }
        lights = merged.ToArray();
        lightBaseIntensities = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
        {
            Light l = lights[i];
            if (l == null) continue;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.Auto;
            // Clamp the base — some VFX prefabs ship with intensity 8-15
            // for "wow" close-up shots which is what tips the per-tile
            // light list past the budget when several trees overlap.
            if (l.intensity > 1.5f) l.intensity = 1.5f;
            lightBaseIntensities[i] = l.intensity;
        }

        nextCheckTime = Time.unscaledTime + Random.Range(0f, checkInterval);
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCheckTime) return;
        nextCheckTime = Time.unscaledTime + checkInterval;

        if (player == null) player = CameraCache.MainTransform;
        if (player == null) return;

        float sqr = (player.position - transform.position).sqrMagnitude;
        float farSqr = farDistance * farDistance;
        float nearSqr = nearDistance * nearDistance;

        if (sqr >= farSqr)
        {
            SetEmission(false, 0f);
        }
        else
        {
            float t = Mathf.Clamp01(1f - (sqr - nearSqr) / Mathf.Max(0.001f, farSqr - nearSqr));
            // Cap the close-range multiplier at 0.4 — the prefab rates
            // were tuned for "show the player how cool this is" and were
            // 2-3x what's actually visible at 5m. 0.4 is still visibly
            // alive without flooding the overdraw budget.
            t = Mathf.Min(t, 0.4f);
            SetEmission(true, t);
        }
    }

    private void SetEmission(bool on, float multiplier)
    {
        if (!on && !emittingNow)
        {
            // Even when fully off in particles, keep lights silenced.
            for (int i = 0; lights != null && i < lights.Length; i++)
            {
                if (lights[i] != null) lights[i].enabled = false;
            }
            return;
        }

        if (systems != null)
        {
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i].ps;
                if (ps == null) continue;
                var em = ps.emission;
                if (on)
                {
                    em.enabled = true;
                    em.rateOverTime = systems[i].baseRate * multiplier;
                }
                else
                {
                    em.enabled = false;
                }
            }
        }

        // Fade lights with the same distance multiplier; full off past
        // far range so distant trees pay zero light cost.
        if (lights != null)
        {
            for (int i = 0; i < lights.Length; i++)
            {
                Light l = lights[i];
                if (l == null) continue;
                if (on)
                {
                    l.enabled = true;
                    l.intensity = lightBaseIntensities[i] * multiplier;
                }
                else
                {
                    l.enabled = false;
                }
            }
        }

        emittingNow = on;
    }
}
