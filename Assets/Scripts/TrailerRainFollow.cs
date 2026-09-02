using UnityEngine;

// Keeps a rain (or snow) particle volume centred above the camera so the weather
// always fills frame during a moving trailer shot, while staying world-vertical
// so the rain falls straight down regardless of where the camera looks.
public class TrailerRainFollow : MonoBehaviour
{
    public Transform target;      // usually the Main Camera
    public float height = 12f;    // how far above the camera the rain volume sits
    public bool keepVertical = true;

    [Header("Splash polish (drops hitting the ground)")]
    [Tooltip("Shrink the ground-splash particles (the pack's are big blobs).")]
    public float splashSizeMultiplier = 0.3f;
    [Tooltip("Shorten splash life so they don't linger.")]
    public float splashLifetimeMultiplier = 0.6f;
    [Tooltip("Only let rain splash on the TERRAIN — stops splashes hanging in the air on trees / the horse / stray colliders.")]
    public bool splashOnlyOnTerrain = true;

    private void Start()
    {
        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
        {
            // Shrink / shorten the splash sub-emitter.
            if (ps.name.ToLowerInvariant().Contains("splash"))
            {
                var m = ps.main;
                m.startSizeMultiplier *= splashSizeMultiplier;
                m.startLifetimeMultiplier *= splashLifetimeMultiplier;
            }

            // Restrict rain collision to the terrain layer so splashes only ever
            // appear on the ground, never mid-air on a tree/rock/horse collider.
            if (splashOnlyOnTerrain && Terrain.activeTerrain != null)
            {
                var col = ps.collision;
                if (col.enabled) col.collidesWith = 1 << Terrain.activeTerrain.gameObject.layer;
            }
        }
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            var c = Camera.main;
            if (c != null) target = c.transform;
            if (target == null) return;
        }

        Vector3 p = target.position;
        p.y += height;
        transform.position = p;
        if (keepVertical) transform.rotation = Quaternion.identity;
    }
}
