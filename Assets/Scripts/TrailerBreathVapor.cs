using UnityEngine;

// Breath steaming in the cold, on the horse and the rider.
//
// Part 2 is winter, and the only thing saying so is the colour of the ground.
// Visible breath is the detail that makes an audience FEEL the cold rather than
// merely register it, and unlike snow or frost it costs one small particle
// system per character.
//
// Added by 'Setup Part 2'; switches itself on with the season.
public class TrailerBreathVapor : MonoBehaviour
{
    [Tooltip("Particle system to emit from. Left empty, a small one is built at runtime.")]
    public ParticleSystem vapor;
    [Tooltip("Where the breath comes from — the muzzle or the head. Left empty, this transform is used.")]
    public Transform mouth;
    [Tooltip("Seconds between breaths. Faster while he is fleeing than while he is standing.")]
    public float interval = 1.15f;
    [Tooltip("Only breathe once the world has turned cold, so it does not steam through the summer act.")]
    public bool requireWinter = true;

    private float _next;
    private TrailerSeasonRide _season;

    private void Start()
    {
        if (mouth == null) mouth = transform;
        _season = Object.FindFirstObjectByType<TrailerSeasonRide>();
        if (vapor == null) vapor = Build();
    }

    private ParticleSystem Build()
    {
        var go = new GameObject("BreathVapor");
        go.transform.SetParent(mouth, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        var main = ps.main;
        main.startLifetime = 1.5f;
        main.startSpeed = 1.1f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
        main.startColor = new Color(1f, 1f, 1f, 0.35f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.04f;      // breath rises
        main.maxParticles = 40;

        var emission = ps.emission; emission.enabled = false;   // driven by Emit()
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 14f;
        shape.radius = 0.04f;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1.6f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        ps.Play();
        return ps;
    }

    private void Update()
    {
        if (vapor == null) return;
        if (requireWinter && _season != null && !IsCold()) return;
        if (Time.time < _next) return;

        _next = Time.time + interval * Random.Range(0.8f, 1.25f);
        vapor.transform.rotation = mouth.rotation;
        vapor.Emit(Random.Range(4, 8));
    }

    // The season driver holds a winter tint; breath before that would be wrong
    // in a summer forest.
    private bool IsCold()
    {
        return _season == null || Shader.GetGlobalFloat("_SeasonIndex") >= 1f;
    }
}
