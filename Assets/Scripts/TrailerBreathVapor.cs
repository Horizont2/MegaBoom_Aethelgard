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
        vapor ??= Build();
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

        // A ParticleSystem added from script has NO material, and an unassigned
        // material renders bright magenta — which is exactly the purple specks
        // that appeared around the horse. Build one, and if no shader resolves,
        // switch the whole effect off rather than showing the error colour.
        var r = go.GetComponent<ParticleSystemRenderer>();
        var mat = BuildVaporMaterial();
        if (mat == null)
        {
            Debug.LogWarning("[Trailer] Breath vapor: no usable particle shader — effect disabled rather than rendered as magenta.");
            Destroy(go);
            enabled = false;
            return null;
        }
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.alignment = ParticleSystemRenderSpace.View;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sortMode = ParticleSystemSortMode.Distance;

        ps.Play();
        return ps;
    }

    private static Material s_vaporMat;

    private static Material BuildVaporMaterial()
    {
        if (s_vaporMat != null) return s_vaporMat;

        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
              ?? Shader.Find("Universal Render Pipeline/Unlit")
              ?? Shader.Find("Sprites/Default");
        if (sh == null) return null;

        s_vaporMat = new Material(sh) { name = "M_BreathVapor (runtime)" };

        // The snowflake texture doubles as a soft round puff; without a texture
        // the quad shows as a hard square.
        var tex = Resources.Load<Texture2D>("Shaders/T_Snowflake");
        if (tex != null)
        {
            if (s_vaporMat.HasProperty("_BaseMap")) s_vaporMat.SetTexture("_BaseMap", tex);
            if (s_vaporMat.HasProperty("_MainTex")) s_vaporMat.SetTexture("_MainTex", tex);
        }

        Color c = new Color(1f, 1f, 1f, 0.35f);
        if (s_vaporMat.HasProperty("_BaseColor")) s_vaporMat.SetColor("_BaseColor", c);
        if (s_vaporMat.HasProperty("_Color")) s_vaporMat.SetColor("_Color", c);
        if (s_vaporMat.HasProperty("_Surface")) s_vaporMat.SetFloat("_Surface", 1f);   // transparent
        if (s_vaporMat.HasProperty("_Blend")) s_vaporMat.SetFloat("_Blend", 0f);       // alpha
        if (s_vaporMat.HasProperty("_ZWrite")) s_vaporMat.SetFloat("_ZWrite", 0f);
        if (s_vaporMat.HasProperty("_SrcBlend")) s_vaporMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (s_vaporMat.HasProperty("_DstBlend")) s_vaporMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        s_vaporMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        s_vaporMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        return s_vaporMat;
    }

    private void Update()
    {
        if (vapor == null || !enabled) return;
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
