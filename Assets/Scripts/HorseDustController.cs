using UnityEngine;

// Kicks up hoof dust behind the horse while it gallops (for the lore trailer /
// evacuation ride). Spawns a dust prefab from the pack, keeps it at a ground
// point just behind the horse, and only emits while the horse is actually
// moving — so the trail dies naturally when it slows or stops.
//
// The dust is spawned UNPARENTED and driven in world space, so the 0.5 scale on
// the horse can't shrink it and the particles stay where they were kicked up,
// leaving a proper trail behind the gallop.
public class HorseDustController : MonoBehaviour
{
    [Tooltip("Dust particle prefab (e.g. Hovl Studio 'Dust loop' / 'Dust ground').")]
    public GameObject dustPrefab;
    [Tooltip("Metres behind the horse pivot to place the dust source (at the rear hooves).")]
    public float behind = 0.7f;
    [Tooltip("Lift the dust source this far off the ground.")]
    public float groundLift = 0.05f;
    [Tooltip("Only emit above this speed (m/s) so it doesn't puff while idle.")]
    public float minSpeed = 2.0f;

    [Header("Tame the pack effect into HOOF dust (it's authored as a big cloud)")]
    [Tooltip("Overall size of the spawned dust.")]
    public float dustScale = 0.5f;
    [Tooltip("Multiplier on particle START SIZE (pack dust starts 2-4m — way too big).")]
    public float sizeMultiplier = 0.28f;
    [Tooltip("Multiplier on particle START SPEED (pack dust shoots up at 7-8 m/s).")]
    public float speedMultiplier = 0.15f;
    [Tooltip("Multiplier on particle LIFETIME (shorter = dies low, doesn't drift to the sky).")]
    public float lifetimeMultiplier = 0.5f;
    [Tooltip("Downward pull so the dust settles instead of rising.")]
    public float gravity = 1.0f;
    [Tooltip("Multiplier on emission rate (thinner puff).")]
    public float emissionMultiplier = 0.6f;

    private ParticleSystem[] _systems;
    private Transform _dust;
    private Vector3 _lastPos;

    private void Start()
    {
        _lastPos = transform.position;
        if (dustPrefab == null) return;

        var go = Instantiate(dustPrefab);
        go.name = dustPrefab.name + " (HoofDust)";
        go.transform.localScale = Vector3.one * dustScale;
        _dust = go.transform;
        _systems = go.GetComponentsInChildren<ParticleSystem>(true);

        // World simulation so the emitter can move and leave a trail behind, and
        // TAME the pack cloud into low, small, short-lived hoof dust.
        foreach (var ps in _systems)
        {
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;   // let dustScale shrink sizes too
            main.playOnAwake = true;
            main.startSizeMultiplier *= sizeMultiplier;
            main.startSpeedMultiplier *= speedMultiplier;
            main.startLifetimeMultiplier *= lifetimeMultiplier;
            main.gravityModifier = gravity;                            // pull it back down
            var em = ps.emission;
            em.rateOverTimeMultiplier *= emissionMultiplier;
            ps.Clear();
            if (!ps.isPlaying) ps.Play();
        }
        PlaceDust();
    }

    private void LateUpdate()
    {
        if (_dust == null || _systems == null) return;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float speed = (transform.position - _lastPos).magnitude / dt;
        _lastPos = transform.position;

        PlaceDust();

        bool moving = speed >= minSpeed;
        foreach (var ps in _systems)
        {
            var em = ps.emission;
            em.enabled = moving;
        }
    }

    private void PlaceDust()
    {
        Vector3 p = transform.position - transform.forward * behind;
        p.y += groundLift;
        _dust.position = p;
        _dust.rotation = transform.rotation;
    }

    private void OnDisable()
    {
        if (_systems == null) return;
        foreach (var ps in _systems)
            if (ps != null) { var em = ps.emission; em.enabled = false; }
    }
}
