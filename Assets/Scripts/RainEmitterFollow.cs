using UnityEngine;

// Anchors a rain (or snow / ash / any weather) ParticleSystem above the player
// so drops always fall from directly overhead without inheriting the camera's
// yaw when the player looks around. The particle system's simulation space is
// forced to World, its emission volume is enlarged to cover a decent radius
// around the player, and the emitter is teleported (not lerped) to prevent
// spawn streaks. Attach this to the same GameObject as the ParticleSystem, or
// leave the reference field empty and it will grab the one on this object.
[DisallowMultipleComponent]
public class RainEmitterFollow : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Falling weather particle system. Leave empty to auto-grab from this GameObject.")]
    public ParticleSystem weatherParticles;
    [Tooltip("Optional splash / ground-hit particle system to also keep centred on the player.")]
    public ParticleSystem splashParticles;

    [Header("Follow Settings")]
    [Tooltip("How high above the player the emitter sits. Rain will fall this distance before hitting the ground.")]
    public float heightAbovePlayer = 25f;
    [Tooltip("Horizontal radius of the emission volume. Bigger = fewer 'pop-in' seams when the player runs, but denser rain costs more perf.")]
    public float coverageRadius = 40f;
    [Tooltip("If true, the emitter's rotation is locked to identity so camera yaw never tilts the rain.")]
    public bool lockRotationUpright = true;

    private Transform player;
    private Vector3 shapeOriginalScale;
    private bool cached;

    private void Awake()
    {
        if (weatherParticles == null) weatherParticles = GetComponent<ParticleSystem>();
    }

    private void OnEnable()
    {
        CacheReferences();
        ApplyEmitterConfig();
    }

    private void CacheReferences()
    {
        if (cached) return;
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
        cached = true;
    }

    private void ApplyEmitterConfig()
    {
        if (weatherParticles == null) return;

        var main = weatherParticles.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;

        // Force a box or hemisphere shape big enough to cover coverageRadius so
        // the player can never outrun the rain volume in a single frame.
        var shape = weatherParticles.shape;
        shape.enabled = true;
        if (shape.shapeType == ParticleSystemShapeType.Box || shape.shapeType == ParticleSystemShapeType.Rectangle)
        {
            shape.scale = new Vector3(coverageRadius * 2f, shape.scale.y, coverageRadius * 2f);
        }
        else
        {
            // Fall back to a box for full coverage.
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(coverageRadius * 2f, 1f, coverageRadius * 2f);
        }
        shapeOriginalScale = shape.scale;

        if (splashParticles != null)
        {
            var s = splashParticles.main;
            s.simulationSpace = ParticleSystemSimulationSpace.World;
        }
    }

    private void LateUpdate()
    {
        if (player == null)
        {
            CacheReferences();
            if (player == null) return;
        }

        Vector3 followPos = new Vector3(
            player.position.x,
            player.position.y + heightAbovePlayer,
            player.position.z);

        // Teleport, don't lerp — otherwise fast running leaves a diagonal
        // trail of raindrops behind the player.
        transform.position = followPos;
        if (lockRotationUpright) transform.rotation = Quaternion.identity;

        if (splashParticles != null)
        {
            splashParticles.transform.position = new Vector3(player.position.x, player.position.y, player.position.z);
            if (lockRotationUpright) splashParticles.transform.rotation = Quaternion.identity;
        }
    }
}
