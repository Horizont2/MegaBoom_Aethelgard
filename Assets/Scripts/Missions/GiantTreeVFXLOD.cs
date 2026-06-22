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
    public int maxParticlesCap = 60;

    private struct PSCache
    {
        public ParticleSystem ps;
        public float baseRate;
    }

    private PSCache[] systems;
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
            // Disable shadow casting on the particle renderer.
            ParticleSystemRenderer rend = ps.GetComponent<ParticleSystemRenderer>();
            if (rend != null)
            {
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }
        }

        // Per-instance phase offset so dozens of trees in a region don't
        // all check distance on the same frame.
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
            SetEmission(true, t);
        }
    }

    private void SetEmission(bool on, float multiplier)
    {
        if (systems == null) return;
        if (!on && !emittingNow) return; // already off
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
        emittingNow = on;
    }
}
