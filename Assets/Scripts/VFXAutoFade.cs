using System.Collections;
using UnityEngine;

// Attach to a spawned VFX instance instead of calling Destroy(go, t). It:
//   1. optionally switches every child ParticleSystem to WORLD simulation space
//      so the effect doesn't whip around when its parent (e.g. the player)
//      rotates — emitted particles stay put in the world instead of being
//      pinned to the spinning emitter transform, and
//   2. fades out GRACEFULLY — after `activeDuration` it stops EMITTING and lets
//      the already-spawned particles finish their own lifetime (so they fade
//      via their colour/size-over-lifetime), then destroys the object. This
//      replaces the hard cut that Destroy(go, t) caused mid-emission.
[DisallowMultipleComponent]
public class VFXAutoFade : MonoBehaviour
{
    private float activeDuration = 2f;
    private bool worldSpace = true;
    private ParticleSystem[] systems;

    public void Configure(float duration, bool world = true)
    {
        activeDuration = Mathf.Max(0.05f, duration);
        worldSpace = world;
    }

    private void Start()
    {
        systems = GetComponentsInChildren<ParticleSystem>(true);
        if (worldSpace && systems != null)
        {
            foreach (var ps in systems)
            {
                if (ps == null) continue;
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
            }
        }
        StartCoroutine(FadeRoutine());
    }

    private IEnumerator FadeRoutine()
    {
        // Play out at full strength for the active window.
        yield return new WaitForSeconds(activeDuration);

        // Stop SPAWNING but keep living particles alive so they fade naturally.
        float maxLife = 0.5f;
        if (systems != null)
        {
            foreach (var ps in systems)
            {
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                var main = ps.main;
                // Longest a particle can still be alive (covers constant & curve modes).
                float life = Mathf.Max(main.startLifetime.constant, main.startLifetime.constantMax);
                if (life > maxLife) maxLife = life;
            }
        }

        // Let the longest-lived particle finish, plus a small buffer, then clean up.
        yield return new WaitForSeconds(maxLife + 0.3f);
        Destroy(gameObject);
    }
}
