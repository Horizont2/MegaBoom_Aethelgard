using UnityEngine;

// Low snow blowing along the ground in the winter biome.
//
// Falling snow says "it is snowing". Snow DRIVEN along the surface says "it is
// cold and open here", and it is the layer the winter biome was missing: without
// something moving at ground level the landscape sits perfectly still no matter
// how much falls from the sky.
//
// Deliberately separate from the falling snow VFX: this rides just above the
// terrain and moves horizontally, so it reads as wind rather than weather.
//
// Created by DayNightCycle in the winter biome; follows the player.
public class WinterGroundDrift : MonoBehaviour
{
    [Tooltip("Who it follows. Left empty, the object tagged Player is used.")]
    public Transform follow;

    [Tooltip("Area the drift covers around the player, in metres. Wider than the camera sees, so it never appears to start at the screen edge.")]
    public float area = 55f;
    [Tooltip("Height above the ground the drift rides at. Knee-high reads as blowing snow; higher reads as fog.")]
    public float height = 0.45f;
    public float particles = 900f;
    [Tooltip("Wind speed the drift is carried at.")]
    public float windSpeed = 7f;
    [Tooltip("Compass direction of the wind, in degrees.")]
    public float windDegrees = 30f;
    [Range(0f, 1f)] public float opacity = 0.32f;

    private ParticleSystem _ps;
    private static Material s_mat;

    public static WinterGroundDrift Create(Transform follow)
    {
        var go = new GameObject("WinterGroundDrift");
        var d = go.AddComponent<WinterGroundDrift>();
        d.follow = follow;
        return d;
    }

    private void Start()
    {
        if (follow == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) follow = p.transform;
        }
        _ps = Build();
    }

    private ParticleSystem Build()
    {
        var go = new GameObject("Drift");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(windSpeed * 0.6f, windSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.35f);
        main.startColor = new Color(1f, 1f, 1f, opacity);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.01f;              // barely settles; the wind owns it
        main.maxParticles = Mathf.CeilToInt(particles * 2f);

        var emission = ps.emission;
        emission.rateOverTime = particles;

        // A flat box hugging the ground. Emitting from a volume rather than a
        // plane keeps the near particles from all sitting at one height, which is
        // what would make it read as a sheet.
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(area, height * 2f, area);
        shape.rotation = Vector3.zero;

        // Blown sideways, not falling.
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        Vector3 dir = Quaternion.Euler(0f, windDegrees, 0f) * Vector3.forward;
        vel.x = new ParticleSystem.MinMaxCurve(dir.x * windSpeed * 0.8f, dir.x * windSpeed);
        vel.z = new ParticleSystem.MinMaxCurve(dir.z * windSpeed * 0.8f, dir.z * windSpeed);
        vel.y = new ParticleSystem.MinMaxCurve(-0.1f, 0.25f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.9f;
        noise.frequency = 0.35f;
        noise.scrollSpeed = 0.6f;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        // Fade in and out, or particles pop into existence at the box edge.
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f),
                          new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var r = go.GetComponent<ParticleSystemRenderer>();
        var mat = BuildMaterial();
        if (mat == null)
        {
            // A ParticleSystem built from script has NO material, and an
            // unassigned one renders bright magenta. Better to have no drift than
            // a field of purple specks.
            Debug.LogWarning("[WinterGroundDrift] No usable particle shader — drift disabled rather than rendered as magenta.");
            Destroy(go);
            return null;
        }
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.alignment = ParticleSystemRenderSpace.View;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.maxParticleSize = 0.05f;
        r.sortMode = ParticleSystemSortMode.Distance;

        ps.Play();
        return ps;
    }

    private static Material BuildMaterial()
    {
        if (s_mat != null) return s_mat;

        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
              ?? Shader.Find("Universal Render Pipeline/Unlit")
              ?? Shader.Find("Sprites/Default");
        if (sh == null) return null;

        s_mat = new Material(sh) { name = "M_WinterGroundDrift (runtime)" };
        var tex = Resources.Load<Texture2D>("VFX/T_Snowflake");
        if (tex != null)
        {
            if (s_mat.HasProperty("_BaseMap")) s_mat.SetTexture("_BaseMap", tex);
            if (s_mat.HasProperty("_MainTex")) s_mat.SetTexture("_MainTex", tex);
        }

        Color c = Color.white;
        if (s_mat.HasProperty("_BaseColor")) s_mat.SetColor("_BaseColor", c);
        if (s_mat.HasProperty("_Color")) s_mat.SetColor("_Color", c);
        if (s_mat.HasProperty("_Surface")) s_mat.SetFloat("_Surface", 1f);
        if (s_mat.HasProperty("_Blend")) s_mat.SetFloat("_Blend", 0f);
        if (s_mat.HasProperty("_ZWrite")) s_mat.SetFloat("_ZWrite", 0f);
        if (s_mat.HasProperty("_SrcBlend")) s_mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (s_mat.HasProperty("_DstBlend")) s_mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        s_mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        s_mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        return s_mat;
    }

    private void LateUpdate()
    {
        if (_ps == null || follow == null) return;

        // Ride the ground under the player, not the player's own height — the
        // drift belongs to the terrain, and following him up a hill or into the
        // air would peel it off the surface.
        Vector3 p = follow.position;
        if (TrailerGroundClamp.TryTerrainY(p, out float gy)) p.y = gy;
        transform.position = p + Vector3.up * height;
    }
}
