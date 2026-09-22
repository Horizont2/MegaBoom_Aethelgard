using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Trailer, shot 1 (0:00-0:10) — the king's statue breaks open.
//
// ==== WHY THE FIRST VERSION READ AS FAKE ====
//
// It showed an EFFECT with no CAUSE. Light appeared next to a statue that was
// visually untouched, and an audience reads that as two unrelated things
// happening at once rather than as stone failing. Three fixes, in order of how
// much each one matters:
//
//  1. THE STONE HAS TO BREAK ON SCREEN. Fractures now crawl across the surface,
//     branch, and arrest at edges (TrailerStatueCrack). The statue trembles from
//     the first crack onward instead of only at the end, sheds dust continuously,
//     and spits chips at the exact points the cracks are advancing through. The
//     light is now something that gets OUT, rather than something that arrives.
//
//  2. LIGHT SHAFTS ARE ONLY VISIBLE BECAUSE OF PARTICULATE. A shaft in clean air
//     is invisible in reality and looks like a plastic ribbon in a game engine.
//     Every ray now drives motes drifting through it, and that single addition
//     does more for believability than any amount of shader work. They also
//     flicker on Perlin noise, punch out with overshoot instead of fading up,
//     and get cut short by anything they hit — a shaft that passes through a
//     wall is the fastest way to lose an audience.
//
//  3. PERIODIC MOTION READS AS MACHINERY. The old handheld was a pair of sines,
//     and the eye catches the repeat even when the viewer cannot say why. All
//     motion here is Perlin, at incommensurate rates per axis, with amplitude
//     driven by tension so the camera gets less steady as the statue gets worse.
//
// ==== CAMERA ====
//
// A trailer's opening shot has to do two jobs at once: hold on the subject, and
// make the viewer feel the operator is a person. So: one uninterrupted push-in
// (a cut would say "here is another angle" instead of "something is coming"),
// the statue held OFF-CENTRE and the look-target damped so framing floats rather
// than locks, a dutch roll that creeps in as tension rises, and a spring recoil
// on every fracture so the camera flinches with the stone.
//
// Everything runs on unscaled time; every generated material is explicit,
// because a script-created ParticleSystem or LineRenderer has no material and
// renders magenta.
[DisallowMultipleComponent]
public class TrailerStatueShot : MonoBehaviour
{
    [Header("Cost")]
    // ==== ONE DIAL, BECAUSE THE COST IS ONE THING ====
    //
    // Everything expensive in this shot is the same kind of expensive: a COUNT.
    // Fissures are line renderers that rebuild their mesh every frame on a
    // transform that moves every frame; shafts are a realtime light and an
    // additive quad each; motes, dust and chips are transparent overdraw; debris
    // is a rigid body apiece. Tuning them one at a time means finding six
    // numbers that happen to agree.
    //
    // They are all multiplied by this instead. At one the shot is what it was
    // authored as; at a half it is the same shot with half of everything, which
    // on a crowd of near-identical elements is a difference nobody can name and
    // the frame time can.
    [Range(0.15f, 1f)]
    [Tooltip("Scales every count in the shot at once — fissures, shafts, motes, dust and debris. Lower it until the editor keeps up; the shot reads the same a long way down.")]
    public float detail = 0.5f;

    // ==== NO FLASHES ====
    //
    // Every fracture fired a vignette-and-aberration punch, and the burst
    // flooded the lens with a full-screen white. Both are full-screen post
    // passes landing on the frames that are already the most expensive in the
    // shot — the punch runs a chromatic aberration pass for the whole build,
    // and the flood is a screen-sized additive overlay on top of the burst.
    //
    // They were also the least missed thing in it: the stone breaking is the
    // shot, and a flash is something put over the top of a shot.
    [Tooltip("Vignette punches on every fracture, and the white flood on the burst. Off: the ending is the letterbox closing on the frame, with no flash over it.")]
    public bool flashes;

    [Header("Sequencing")]
    [Tooltip("OFF when this shot is chained after another — the sequencer starts it on cue instead of it firing the moment its rig switches on.")]
    public bool autoPlay = true;

    // Read by TrailerShotChain to know when to move on. A shot that cannot say
    // when it is done can only be followed by a guessed delay, and a guessed
    // delay drifts the moment any beat is retuned.
    public bool IsFinished { get; private set; }

    [Header("Scene")]
    public Transform statue;
    public Camera shotCamera;

    [Header("Beats (seconds)")]
    [Tooltip("Dead-quiet establish before anything happens. The stillness is what makes the first crack land.")]
    public float establish = 2.6f;
    [Tooltip("From first fracture to full burst.")]
    public float buildDuration = 4.8f;
    public float outFade = 0.4f;

    [Header("Camera Move")]
    public float startDistance = 15f;
    public float endDistance = 5.2f;
    public float startHeight = 3.4f;
    public float endHeight = 2.4f;
    [Tooltip("Degrees of drift around the statue. Small — this is a push, not an orbit.")]
    public float orbitDrift = 7f;
    public float startFov = 36f;
    public float endFov = 48f;
    [Tooltip("Statue's horizontal position in frame, 0.5 = dead centre. Off-centre framing is what stops it looking like a product turntable.")]
    [Range(0.25f, 0.75f)] public float framingBias = 0.42f;
    [Tooltip("How fast the aim catches up to the camera. Lower = more float.")]
    public float lookDamping = 2.2f;
    [Tooltip("Max dutch roll in degrees at full tension.")]
    public float maxRoll = 2.4f;
    [Tooltip("Handheld amplitude in metres at rest. Grows with tension.")]
    public float handheldBase = 0.018f;
    public float handheldAtPeak = 0.075f;

    [Header("Fracture")]
    [Range(1, 8)] public int seedCracks = 4;
    [Tooltip("Hard ceiling on SEED fissures — the ones that carry a realtime point light and a shaft. Each one costs a light, and past a dozen they stop adding anything visible while the shadow atlas starts thrashing.")]
    [Range(2, 24)] public int maxCracks = 10;
    [Tooltip("Hard ceiling on ALL fissures, branches included.\n\nBranches used to ignore maxCracks entirely: three forks per crack, two generations deep, so ten seeds became a hundred and thirty line renderers on a transform that moves every frame — which is the freeze at the end of the shot. Detail past roughly thirty reads as a smear anyway.")]
    [Range(4, 64)] public int maxTotalCracks = 30;
    [Tooltip("Seconds between fracture advances at the start. Cracks accelerate as pressure builds.")]
    public float stepIntervalStart = 0.16f;
    public float stepIntervalEnd = 0.035f;
    public float crackWidthScale = 1f;

    [Header("Light")]
    // Ember, not violet.
    //
    // A violet shaft with a near-white core goes PINK the moment it is blended
    // additively: red and blue saturate while green lags behind, and pink is the
    // one colour that cannot read as menacing. Deep ember red with a hot amber
    // core reads as a furnace behind the stone, and sits against the cold blue
    // moonlight instead of dissolving into it.
    //
    // For the violet corruption used elsewhere in the game, set lightColor to
    // roughly (0.18, 0.10, 0.55) and coreColor to (0.55, 0.70, 1.0) — a COLD
    // blue-violet with a low red channel, which stays spectral rather than pink.
    public Color lightColor = new Color(0.60f, 0.085f, 0.04f, 1f);
    public Color coreColor = new Color(1f, 0.52f, 0.20f, 1f);
    public float lightIntensity = 22f;
    [Tooltip("Shaft length in metres before occlusion trims it.")]
    public float rayLength = 24f;
    public float rayWidth = 0.26f;
    [Tooltip("How far the shafts lean toward the camera when a crack opens, 0 = straight out of the stone. They keep that direction for the rest of the shot; the sweep comes from the camera moving past them.")]
    [Range(0f, 1f)] public float rayLeanToCamera = 0.5f;
    [Tooltip("Keep this low. Fast, deep flicker is what makes shafts look like a disco rig; a menacing light barely moves and only breathes.")]
    [Range(0f, 1f)] public float rayFlicker = 0.10f;
    [Tooltip("How far the shafts push toward the hot core colour. Additive blending SUMS overlapping shafts, so anything high here saturates every channel and the light turns white — which is exactly what stops it looking dangerous. Keep it low and let only the crack mouths burn.")]
    [Range(0f, 1f)] public float rayHeat = 0.18f;
    [Tooltip("Peak opacity of a single shaft. Low, because they stack: six faint shafts crossing read far darker and more solid than six bright ones, which just blow out to white.")]
    [Range(0.05f, 1f)] public float rayOpacity = 0.22f;
    [Tooltip("Flicker speed. Slow is ominous, fast is a fault in a strip light.")]
    public float flickerSpeed = 1.4f;
    [Tooltip("Motes drifting through each shaft. This is what makes a shaft look volumetric rather than printed.")]
    [Range(0, 60)] public int motesPerRay = 22;
    [Tooltip("Longest a shaft may be, per metre the camera is from the statue. The push ends about five metres out, where a 24 m beam covers most of the frame; this keeps what a shaft costs roughly the same at the end of the push as at the start.")]
    [Range(0.4f, 4f)] public float shaftLengthPerMetre = 1.1f;
    [Tooltip("How far the shafts and the dust are eased back once the lens is right on the statue. 1 = no easing; lower = less blending to pay for, and less blow-out from ten additive shafts crossing at close range.")]
    [Range(0.15f, 1f)] public float nearShaftFalloff = 0.45f;

    [Header("Collapse")]
    public GameObject[] debrisPrefabs;
    [Range(0, 60)] public int debrisCount = 22;
    [Tooltip("Statue tremor at full tension, in metres.")]
    public float tremorAtPeak = 0.055f;
    [Tooltip("Hide the statue mid-burst. Left OFF now that the shot ends on the flare — there is no aftermath frame to hide an intact statue in, so the swap would only pop.")]
    public bool vanishOnBurst = false;
    [Header("Framing")]
    [Tooltip("Work out the end distance from the statue's actual size instead of trusting endDistance. A hand-typed distance frames whatever the statue's scale happens to be.")]
    public bool autoFrame = true;
    [Tooltip("How much of the frame height the statue's upper body should fill when the push finishes.")]
    [Range(0.3f, 1.1f)] public float framingHeightFraction = 0.78f;
    [Tooltip("Metres of clearance kept between the lens and the statue's widest point. The framing maths solves for composition and knows nothing about how wide the stone is, so without this it will happily park the camera inside it.")]
    public float clearance = 2.5f;

    [Header("Ending — the shaft takes the lens")]
    // The shot ends ON the burst, not after it.
    //
    // Everything that came after — the camera shoved back, craning up onto rubble
    // and a column of light — was a second, weaker shot glued to the end of a good
    // one, and it is why the camera kept finishing somewhere odd looking at
    // nothing. A trailer's opening beat should hand over at its peak. So the last
    // fracture fires a shaft straight down the barrel: it floods the lens, burns
    // out to white, and collapses to black. The blackout IS the cut.
    [Tooltip("Seconds from the burst to full black. This whole window is the transition to the next shot.")]
    public float pierceDuration = 1.15f;
    [Tooltip("How hard the blast shoves the camera back as the shaft hits, in metres.")]
    public float blastShove = 1.6f;

    [Header("Audio")]
    // Own sounds, not borrowed combat ones. A shockwave standing in for cracking
    // stone is the kind of thing an audience cannot name but does notice.
    public string dreadBed = AudioID.Trailer_Dread;
    public string groanSound = AudioID.Trailer_StoneStress;
    public string crackSound = AudioID.Trailer_StoneCrack;
    public string burstSound = AudioID.Trailer_StoneBurst;
    public string rubbleSound = AudioID.Trailer_Rubble;
    // Reuses the trailer's existing riser event rather than declaring a second
    // id for the same FMOD path.
    public string riserSound = AudioID.Trailer_RiserToStrike;

    // ---- runtime ----
    private Transform camT;
    private Bounds bounds;
    private Vector3 center;
    private float baseAzimuth;
    private Material rayMat, crackMat;
    private readonly List<TrailerStatueCrack> cracks = new List<TrailerStatueCrack>();
    private readonly List<Shaft> shafts = new List<Shaft>();
    private readonly List<Renderer> statueRenderers = new List<Renderer>();
    private ParticleSystem sheetDust, chipBurst;
    private Vector3 statueHome;
    private float tension;              // 0..1, the single value everything reads from
    private Vector3 recoilVel, recoilOffset;
    private float noiseSeed;
    private Vector3 lookTarget;
    private float pushSeconds;      // how long the approach lasts — the dolly stops when the statue does
    private float burstStartedAt = -1f;
    private float resolvedEndDistance;

    private class Shaft
    {
        public LineRenderer line;
        public Light glow;
        public ParticleSystem motes;
        public Vector3 originLocal, normal;   // local to the statue, so the shaft rides the tremor
        // Fixed at birth and never re-aimed. Shafts that chase the camera every
        // frame swing around the screen like searchlights; real light from a
        // fixed source is still, and it is the CAMERA moving past it that makes
        // it sweep across frame.
        public Vector3 dirLocal;
        public Vector3 Origin(Transform statue) => statue.TransformPoint(originLocal);
        public float bornAt = -1f;
        public float phase;
        public float widthScale;
    }

    // Every count in the shot, put through the one dial. Floored at one so a low
    // setting thins the shot rather than emptying it.
    private int Scaled(int n) { return Mathf.Max(1, Mathf.RoundToInt(n * Mathf.Clamp01(detail))); }

    private void Start()
    {
        if (statue == null) { Debug.LogWarning("[StatueShot] No statue assigned."); enabled = false; return; }
        shotCamera = shotCamera != null ? shotCamera : Camera.main;
        if (shotCamera == null) { Debug.LogWarning("[StatueShot] No camera."); enabled = false; return; }

        camT = shotCamera.transform;
        noiseSeed = Random.Range(0f, 1000f);
        statue.GetComponentsInChildren(statueRenderers);

        bounds = ComputeBounds(statue);
        center = bounds.center;
        statueHome = statue.position;
        lookTarget = center;

        Vector3 flat = camT.position - center; flat.y = 0f;
        baseAzimuth = flat.sqrMagnitude > 0.01f ? Mathf.Atan2(flat.z, flat.x) * Mathf.Rad2Deg : Random.Range(0f, 360f);

        pushSeconds = establish + buildDuration;
        resolvedEndDistance = ResolveEndDistance();

        ResolveSurfaceMask();
        HoldStatueKinematic();

        // The numbers this shot is framed from, printed once. Every framing
        // decision below is derived from them, so when the framing is wrong
        // these are the first thing worth reading rather than the last.
        Debug.Log($"[StatueShot] Statue measures {bounds.size.x:0.0} x {bounds.size.y:0.0} x {bounds.size.z:0.0} m " +
                  $"(scale {statue.lossyScale.x:0.00}), centre {center}. Push {startDistance:0.0} -> " +
                  $"{resolvedEndDistance:0.0} m, never closer than {MinOrbitRadius:0.0}.");

        BuildMaterials();
        BuildStatueDust();
        StartCoroutine(WarmDebris());
        if (autoPlay) Play();
    }

    // Every raycast that walks the stone — the crack advance, the branch catch,
    // the seed search — only ever wants to hit the STATUE, and used to be fired
    // against ~0. On a terrain full of trees and rocks that is a full scene query
    // per step per crack for an answer that is thrown away unless it landed on
    // the statue. Narrowed to whichever layers the statue's own colliders sit on.
    private int surfaceMask = ~0;

    private void ResolveSurfaceMask()
    {
        int mask = 0;
        foreach (var c in statue.GetComponentsInChildren<Collider>(true))
            if (c != null) mask |= 1 << c.gameObject.layer;
        if (mask != 0) surfaceMask = mask;
    }

    // ==== A STATIC COLLIDER THAT MOVES EVERY FRAME IS THE EXPENSIVE KIND ====
    //
    // UpdateStatue writes statue.position on every frame of the shot, and the
    // statue carries a NON-CONVEX MeshCollider. To PhysX a collider with no
    // Rigidbody is part of the static world, and moving one makes it re-insert
    // that actor into the static broadphase — for a concave mesh, every frame,
    // while the crack walk is querying against it.
    //
    // A kinematic Rigidbody says "this moves, expect it to": the collider goes
    // into the dynamic tree, where being moved is free. Nothing else changes —
    // kinematic bodies ignore gravity and forces, and a non-convex mesh collider
    // is legal on one.
    private Rigidbody tremorBody;
    private bool tremorBodyIsOurs;

    private void HoldStatueKinematic()
    {
        Collider col = statue.GetComponentInChildren<Collider>(true);
        if (col == null) return;

        tremorBody = col.GetComponentInParent<Rigidbody>();
        if (tremorBody != null) return;              // it already has one; leave it alone

        tremorBody = col.gameObject.AddComponent<Rigidbody>();
        tremorBody.isKinematic = true;
        tremorBody.useGravity = false;
        tremorBodyIsOurs = true;
    }

    // Where the push should STOP.
    //
    // The first version ended at a hand-typed 5.2 metres, which frames whatever
    // the statue's scale happens to be — at this statue's size that put the lens
    // inside the torso, filling the screen with an unreadable slab of stone. Solve
    // it from the subject instead: to make a world height H fill fraction f of the
    // frame at vertical FOV t, the camera has to sit H / (2f * tan(t/2)) away.
    private float ResolveEndDistance()
    {
        if (!autoFrame) return endDistance;

        float subjectHeight = bounds.size.y * SubjectFraction;
        float d = subjectHeight / (2f * Mathf.Max(0.05f, framingHeightFraction)
                                   * Mathf.Tan(endFov * 0.5f * Mathf.Deg2Rad));
        return Mathf.Max(d, MinOrbitRadius);
    }

    // The part of the statue the shot is actually about: head and shoulders.
    private const float SubjectFraction = 0.45f;

    // The closest the lens may ever get to the statue's axis.
    //
    // The framing maths solves for how the subject SITS in frame and knows
    // nothing about how wide the statue is, so on a broad or heavily-scaled
    // statue it happily asks for a distance that is inside the stone. Measured
    // off the actual horizontal footprint so it holds whatever the statue is, and
    // applied to the LIVE distance every frame rather than only to the end value
    // — otherwise the push could still clip a shoulder on the way in.
    private float MinOrbitRadius =>
        new Vector2(bounds.extents.x, bounds.extents.z).magnitude + clearance;

    private static Bounds ComputeBounds(Transform root)
    {
        var rs = root.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(root.position, Vector3.one * 4f);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    private void BuildMaterials()
    {
        rayMat = MakeUnlit(additive: true);
        crackMat = MakeUnlit(additive: false);   // the fissure must be able to go DARKER than the stone
    }

    private static Material MakeUnlit(bool additive)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        var m = new Material(sh);
        if (m.HasProperty("_Surface"))
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", additive
                ? (int)UnityEngine.Rendering.BlendMode.One
                : (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        m.color = Color.white;
        return m;
    }

    // Dust sheeting off the whole statue, and a chip emitter re-aimed at whichever
    // fracture is advancing. Continuous shedding is most of what makes the stone
    // look like it is under load rather than sitting still next to an effect.
    private void BuildStatueDust()
    {
        // ==== THE STATUE IS SCALED 3.2, AND PARTICLES ARE LOCAL ====
        //
        // This was parented to the statue so it would ride the tremor. The
        // statue is instantiated at 3.2 — TrailerStatueCrack already carries a
        // scaleComp field for exactly that reason — and a ParticleSystem's
        // shape and start size are LOCAL, so both got multiplied by it: a box
        // three times wider than the statue, throwing particles three times the
        // size they were written as.
        //
        // A hundred of those, nearly a metre across, alpha-blended, filmed from
        // five metres, is a pale wall across the whole frame with the statue
        // somewhere behind it — and a pale wall of large transparent quads is
        // also the frame rate. That is the white object, and it is why the
        // statue "disappeared".
        //
        // Parented to the rig instead, which is unscaled, so every number in
        // here means metres. The tremor is five centimetres; nothing is lost by
        // the dust not riding it.
        sheetDust = MakeParticles("SheetDust", transform, new Color(0.52f, 0.48f, 0.56f, 0.30f));
        var m = sheetDust.main;
        m.startLifetime = 2.6f;
        m.startSpeed = 0.35f;
        m.startSize = 0.28f;
        m.gravityModifier = 0.12f;
        m.maxParticles = Scaled(220);
        var sh = sheetDust.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = bounds.size * 0.85f;
        sheetDust.transform.position = center;
        sheetDust.transform.rotation = Quaternion.identity;
        var em = sheetDust.emission;
        em.rateOverTime = 0f;    // driven by tension
        sheetDust.Play();

        chipBurst = MakeParticles("Chips", transform, new Color(0.42f, 0.38f, 0.44f, 0.95f));
        var cm = chipBurst.main;
        cm.startLifetime = 1.4f;
        cm.startSpeed = 2.6f;
        cm.startSize = 0.06f;
        cm.gravityModifier = 1.1f;
        cm.maxParticles = Scaled(260);
        var ce = chipBurst.emission;
        ce.rateOverTime = 0f;
        var cs = chipBurst.shape;
        cs.shapeType = ParticleSystemShapeType.Cone;
        cs.angle = 32f;
        cs.radius = 0.05f;
        chipBurst.Play();
    }

    // A particle material with no texture draws each particle as a hard-edged
    // QUAD. That is the whole reason the dust and chips looked like flying
    // Minecraft blocks — nothing to do with the meshes, everything to do with a
    // missing sprite. Generated once here so the component stays drop-in.
    private static Texture2D s_softDot;
    private static Texture2D SoftDot()
    {
        if (s_softDot != null) return s_softDot;

        const int N = 64;
        s_softDot = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        float c = (N - 1) * 0.5f;
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            // Squared falloff: a linear ramp still shows a visible disc edge.
            float a = Mathf.Clamp01(1f - d);
            a *= a;
            px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        s_softDot.SetPixels32(px);
        s_softDot.Apply(true);
        return s_softDot;
    }

    private ParticleSystem MakeParticles(string name, Transform parent, Color tint)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startColor = tint;
        main.useUnscaledTime = true;

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;

        // Shrink and fade out rather than blinking out of existence at the end
        // of the lifetime, which is the other half of the "cheap particles" look.
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.35f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.15f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // Tumble, so chips read as fragments rather than as sprites sliding.
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-180f, 180f);

        var pr = ps.GetComponent<ParticleSystemRenderer>();
        Shader psh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (psh == null) psh = Shader.Find("Sprites/Default");
        if (psh != null)
        {
            var mat = new Material(psh);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", SoftDot());
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", SoftDot());
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            pr.material = mat;
        }
        else pr.enabled = false;

        return ps;
    }

    // ======================= the shot =======================

    public void Play()
    {
        if (IsFinished) return;
        if (!enabled)
        {
            // Start() switches this off when the statue or camera is missing.
            // Without saying so, the sequencer simply waits out its timeout and
            // reports "never finished", which describes the symptom and not the
            // cause.
            Debug.LogWarning("[StatueShot] Asked to play but the component is disabled — " +
                             "statue or camera was missing at Start. Re-run Setup Shot 1.");
            IsFinished = true;
            return;
        }
        StartCoroutine(PlayShot());
    }

    private IEnumerator PlayShot()
    {
        TrailerLogGuard.Arm();
        var polish = TrailerCinematicPolish.GetOrCreate();
        polish.OpenTrailer();
        TrailerAudio.SilenceStaleBeds();

        // The dread bed runs under the whole shot. Without something holding the
        // low end, the silences between cracks read as the audio having stopped
        // rather than as the shot holding its breath.
        if (AudioManager.Instance != null && !string.IsNullOrEmpty(dreadBed))
            AudioManager.Instance.PlaySFX3D(dreadBed, center);

        float total = establish + buildDuration + pierceDuration;
        float t = 0f;
        bool burst = false;
        float nextCrackStep = establish;
        int seeded = 0;

        if (AudioManager.Instance != null && !string.IsNullOrEmpty(groanSound))
            AudioManager.Instance.PlaySFX3D(groanSound, center);

        while (t < total)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;

            // Tension is the one dial. Everything — tremor, dust, light, camera
            // steadiness, roll, fracture speed — reads from it, so the whole shot
            // escalates together instead of as separate effects on separate clocks.
            tension = Mathf.Clamp01((t - establish) / Mathf.Max(0.01f, buildDuration));
            if (burst) tension = 1f;

            UpdateCamera(t, total, dt);
            UpdateStatue(dt);
            UpdateShafts(t);
            UpdatePierce(t);

            if (!burst && t >= establish && t < establish + buildDuration && t >= nextCrackStep)
            {
                // Seed the first cracks in the opening moments, then let them run.
                if (seeded < Scaled(seedCracks) && (seeded == 0 || Random.value < 0.5f))
                {
                    SeedCrack();
                    seeded++;
                }
                StepCracks();

                // Fractures accelerate as pressure rises: the gaps between
                // events shorten, which is what an audience hears as "worsening".
                nextCrackStep = t + Mathf.Lerp(stepIntervalStart, stepIntervalEnd, tension);
            }

            if (!burst && t >= establish + buildDuration)
            {
                burst = true;
                burstStartedAt = t;
                StartCoroutine(BurstRoutine());
            }

            yield return null;
        }

        // The bars are shut, which IS the black — no fade needed, and fading
        // black onto black would only hold the shot open.
        TrailerCinematicPolish.GetOrCreate().SetFlash(new Color(0f, 0f, 0f, 0f));
        TearDown();

        // A beat of held black so the shot has an out point rather than ending
        // on its own last frame of motion.
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, outFade));
        IsFinished = true;
    }

    // ======================= camera =======================

    private void UpdateCamera(float t, float total, float dt)
    {
        // The dolly runs only while there is something to approach. It used to be
        // spread over the WHOLE shot, so for the last three seconds — after the
        // statue had already gone — the camera was still creeping toward an empty
        // patch of air. That is the "it ends up somewhere odd filming nothing".
        float u = Mathf.Clamp01(t / Mathf.Max(0.01f, pushSeconds));
        // Smootherstep: zero velocity AND zero acceleration at both ends, so the
        // push never announces its start or its stop.
        float e = u * u * u * (u * (u * 6f - 15f) + 10f);

        float az = (baseAzimuth + Mathf.Lerp(0f, orbitDrift, e)) * Mathf.Deg2Rad;
        float dist = Mathf.Max(Mathf.Lerp(startDistance, resolvedEndDistance, e), MinOrbitRadius);
        float h = Mathf.Lerp(startHeight, endHeight, e);

        // The blast shoves the lens back for the fraction of a second before the
        // light takes the frame. There is no "afterwards" any more — the shot
        // hands over at its peak.
        float post = burstStartedAt >= 0f ? t - burstStartedAt : -1f;
        float shove = 0f, fovKick = 0f;
        if (post >= 0f)
        {
            // Attack THEN decay. Both of these used to reach full value at
            // post == 0, which is an instantaneous 1.6 m jump in position and a
            // 7-degree step in focal length on a single frame — a teleport and a
            // lens change, read together as the camera lurching into the statue.
            // A blast still arrives fast; it does not arrive in zero time.
            float attack = 1f - Mathf.Exp(-post * 26f);
            float knock = Mathf.Exp(-post * 3.2f);
            shove = blastShove * attack * (0.45f + 0.55f * knock);
            fovKick = 7f * attack * Mathf.Exp(-post * 2.6f);
        }

        Vector3 pos = new Vector3(center.x + Mathf.Cos(az) * (dist + shove),
                                  bounds.min.y + h,
                                  center.z + Mathf.Sin(az) * (dist + shove));
        pos = PushOutOfStone(pos, az);

        // Perlin handheld, incommensurate per axis so it never visibly repeats,
        // and louder as the statue gets worse.
        float amp = Mathf.Lerp(handheldBase, handheldAtPeak, tension);
        Vector3 shake = new Vector3(
            (Mathf.PerlinNoise(noiseSeed + t * 1.7f, 0f) - 0.5f),
            (Mathf.PerlinNoise(0f, noiseSeed + t * 2.3f) - 0.5f),
            (Mathf.PerlinNoise(noiseSeed + t * 1.1f, noiseSeed) - 0.5f)) * (amp * 2f);

        // Spring-damped recoil from each fracture. A flinch that decays is read
        // as a reaction; an instant offset is read as a glitch.
        recoilVel = Vector3.Lerp(recoilVel, Vector3.zero, 1f - Mathf.Exp(-9f * dt));
        recoilOffset += recoilVel * dt;
        recoilOffset = Vector3.Lerp(recoilOffset, Vector3.zero, 1f - Mathf.Exp(-7f * dt));

        camT.position = pos + camT.right * shake.x + camT.up * shake.y + camT.forward * shake.z + recoilOffset;

        // Damped aim, settling on the head and shoulders — the part of the statue
        // the shot is actually about.
        float subjectY = bounds.max.y - bounds.size.y * SubjectFraction * 0.5f;
        Vector3 desired = new Vector3(center.x, Mathf.Lerp(center.y, subjectY, e), center.z);
        lookTarget = Vector3.Lerp(lookTarget, desired, 1f - Mathf.Exp(-lookDamping * dt));

        Quaternion look = Quaternion.LookRotation((lookTarget - camT.position).normalized);
        // Bias the subject off-centre by yawing slightly off the aim.
        float yawBias = (0.5f - framingBias) * shotCamera.fieldOfView;
        // Dutch roll creeps in with tension — rarely noticed consciously, always felt.
        float roll = maxRoll * tension * (0.6f + 0.4f * (Mathf.PerlinNoise(noiseSeed + t * 0.6f, 4f) - 0.5f) * 2f);
        camT.rotation = look * Quaternion.Euler(0f, yawBias, roll);

        shotCamera.fieldOfView = Mathf.Lerp(startFov, endFov, e) + fovKick;
    }

    // ==== THE LENS MAY NOT BE INSIDE THE STATUE ====
    //
    // MinOrbitRadius keeps the camera outside a CYLINDER drawn around the
    // statue's bounding box, which is the right idea and an approximation: the
    // bounds are a box around everything the statue's renderers cover, and the
    // radius is taken from its horizontal diagonal. If the real geometry reaches
    // further than that box in any direction — a plinth, an outstretched arm, a
    // base that was modelled off-centre — the camera can still be inside the
    // stone while the arithmetic says it is two and a half metres clear.
    //
    // From inside, a lit statue at point-blank range is a pale mass filling the
    // frame with the world showing past its edges, which is exactly what the
    // frame looks like. So rather than trust the box, ask the colliders: while
    // anything of the statue is within `clearance` of the lens, walk the lens
    // straight back out along its own bearing.
    private Vector3 PushOutOfStone(Vector3 pos, float azimuth)
    {
        Vector3 outward = new Vector3(Mathf.Cos(azimuth), 0f, Mathf.Sin(azimuth));

        // Measured by raycasting INWARD, from well outside the statue toward its
        // axis at the lens's own height. The first thing the ray meets is the
        // outer surface along this exact bearing, which is the number the
        // bounding box was only ever approximating.
        //
        // Inward and not outward on purpose: the statue's MeshCollider is
        // non-convex, and a ray that starts inside one leaves through a backface
        // — which Unity does not report. Coming from outside there is always a
        // front face to hit. (Collider.ClosestPoint is no use here for the same
        // reason: it is only defined for convex colliders.)
        float probe = MinOrbitRadius + 30f;
        Vector3 from = new Vector3(center.x, pos.y, center.z) + outward * probe;

        if (Physics.Raycast(from, -outward, out RaycastHit hit, probe, surfaceMask, QueryTriggerInteraction.Ignore)
            && hit.transform.IsChildOf(statue))
        {
            float surfaceRadius = probe - hit.distance;
            float want = surfaceRadius + clearance;

            Vector3 flat = pos - center; flat.y = 0f;
            if (flat.magnitude < want)
                return new Vector3(center.x, pos.y, center.z) + outward * want;
        }

        return pos;
    }

    private void Kick(Vector3 fromPoint, float strength)
    {
        Vector3 dir = (camT.position - fromPoint).normalized;
        recoilVel += (dir + Random.insideUnitSphere * 0.4f) * strength;
    }

    // ======================= statue =======================

    private void UpdateStatue(float dt)
    {
        // Perlin tremor, not random jitter: random reads as noise, Perlin reads
        // as strain — the stone straining rather than the transform vibrating.
        float a = tremorAtPeak * tension * tension;
        float tt = Time.unscaledTime;
        Vector3 tremor = new Vector3(
            Mathf.PerlinNoise(tt * 13f, noiseSeed) - 0.5f,
            Mathf.PerlinNoise(noiseSeed, tt * 17f) - 0.5f,
            Mathf.PerlinNoise(tt * 11f, tt * 7f) - 0.5f) * (a * 2f);
        statue.position = statueHome + tremor;

        if (sheetDust != null)
        {
            var em = sheetDust.emission;
            // Thinned as the lens arrives, for the same reason the shafts are:
            // the camera ends up INSIDE this cloud, where a couple of hundred
            // soft billboards stop being dust and become a screen-sized smear
            // that costs a screen-sized amount of blending.
            float near = Mathf.Clamp(Vector3.Distance(camT.position, center) / Mathf.Max(1f, startDistance),
                                     nearShaftFalloff, 1f);
            em.rateOverTime = Mathf.Lerp(0f, 90f, tension * tension) * near;
        }
    }

    // A seed is a fissure that gets its own light and its own shaft. Counted
    // apart from `cracks`, which now also holds the branches — without that the
    // branches would eat the seed budget and the stone would stop opening new
    // fissures after the first two or three forked.
    private int seedCount;

    private void SeedCrack()
    {
        if (seedCount >= Scaled(maxCracks)) return;
        if (!FindSurfacePoint(out Vector3 pos, out Vector3 nrm)) return;

        int before = cracks.Count;
        SpawnCrack(pos, nrm, 0);
        if (cracks.Count == before) return;      // total cap reached; no shaft without a crack
        seedCount++;

        var shaft = BuildShaft(pos, nrm);
        shafts.Add(shaft);

        Kick(pos, 0.55f);
        if (flashes) TrailerCinematicPolish.GetOrCreate().ImpactPunch(0.3f, 0.22f);
        if (AudioManager.Instance != null && !string.IsNullOrEmpty(crackSound))
            AudioManager.Instance.PlaySFX3D(crackSound, pos);
    }

    // ==== THE CAP HAS TO BE HERE, NOT ONLY ON THE REFILL ====
    //
    // maxCracks was only ever tested where StepCracks re-seeds an arrested
    // fissure. BRANCHING went around it: every crack may fork three times and
    // each fork may fork three times again, so ten seeds became 10 + 30 + 90 =
    // a hundred and thirty live fractures, and nothing anywhere said no.
    //
    // That is the freeze at the end of the shot, and it explains its shape —
    // it arrives gradually and gets worse, because the cost is proportional to
    // how many cracks exist and they keep multiplying until the burst. Each one
    // is a GameObject with a view-aligned LineRenderer that rebuilds its mesh
    // every frame, parented to a statue whose transform moves every frame; and
    // every StepCracks call walks the whole list and raycasts once or twice per
    // live crack — at the shortest step interval that is thousands of raycasts a
    // second against the statue's NON-CONVEX MeshCollider, which PhysX has to
    // refit each time the statue trembles.
    //
    // Capping it here bounds all of that at once. A dozen fissures is already
    // more than the frame can show — past that they overlap into a smear and
    // cost real milliseconds to say nothing new.
    private void SpawnCrack(Vector3 pos, Vector3 nrm, int generation)
    {
        if (cracks.Count >= Scaled(maxTotalCracks)) return;

        var go = new GameObject($"Crack_{cracks.Count}");
        var c = go.AddComponent<TrailerStatueCrack>();
        c.generation = generation;
        c.glowColor = lightColor;
        c.surfaceMask = surfaceMask;
        c.Init(statue, pos, nrm, crackMat, OnCrackStep);
        cracks.Add(c);
    }

    private void StepCracks()
    {
        int live = 0;
        for (int i = 0; i < cracks.Count; i++)
        {
            var c = cracks[i];
            if (c == null || c.Finished) continue;
            live++;

            if (c.Advance() && c.ShouldBranch())
            {
                c.GetBranchSeed(out Vector3 bp, out Vector3 bn);
                SpawnCrack(bp, bn, c.generation + 1);
            }
            c.SetHeat(tension, crackWidthScale);
        }

        // Everything has arrested but the burst has not arrived — open a new
        // fracture so the stone never goes quiet mid-build.
        //
        // CAPPED. Cracks arrest at edges constantly, so this fired on almost every
        // step and each new crack brings its own point light: the shot was ending
        // up with over a hundred realtime lights, which blows the shadow atlas and
        // is most of why the whole editor crawled. A dozen fissures is already
        // more than the frame can show. SeedCrack tests the cap itself now.
        if (live == 0 && tension < 0.95f) SeedCrack();
    }

    // Chips fly from wherever the fracture is actually advancing, so the debris
    // is tied to a visible cause instead of puffing from the statue generally.
    private void OnCrackStep(Vector3 pos, Vector3 nrm)
    {
        if (chipBurst == null) return;
        chipBurst.transform.position = pos;
        chipBurst.transform.rotation = Quaternion.LookRotation(nrm);
        chipBurst.Emit(Random.Range(1, 4));
    }

    private bool FindSurfacePoint(out Vector3 pos, out Vector3 nrm)
    {
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + 4f;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            // Bias toward the camera-facing side and the upper body — where the
            // audience is already looking.
            float ang = baseAzimuth + Random.Range(-90f, 90f);
            float h = Mathf.Lerp(bounds.min.y + bounds.size.y * 0.30f,
                                 bounds.max.y - bounds.size.y * 0.06f, Random.value);

            Vector3 from = new Vector3(center.x + Mathf.Cos(ang * Mathf.Deg2Rad) * radius, h,
                                       center.z + Mathf.Sin(ang * Mathf.Deg2Rad) * radius);
            Vector3 dir = new Vector3(center.x - from.x, 0f, center.z - from.z).normalized;

            if (Physics.Raycast(from, dir, out RaycastHit hit, radius * 2.5f, surfaceMask, QueryTriggerInteraction.Ignore)
                && hit.transform.IsChildOf(statue))
            {
                pos = hit.point + hit.normal * 0.02f;
                nrm = hit.normal;
                return true;
            }
        }
        pos = center; nrm = Vector3.up;
        return false;
    }

    // ======================= shafts =======================

    private Shaft BuildShaft(Vector3 pos, Vector3 nrm)
    {
        var root = new GameObject($"Shaft_{shafts.Count}");
        root.transform.SetParent(statue, true);
        root.transform.position = pos;

        // Point it out of the crack, leaned toward the camera's side so it is
        // actually visible, then leave it alone for the rest of the shot.
        Vector3 toCam = (camT.position - pos).normalized;
        Vector3 worldDir = Vector3.Slerp(nrm.normalized, toCam, rayLeanToCamera).normalized;
        worldDir = Quaternion.AngleAxis(Random.Range(-14f, 14f), camT.up) * worldDir;
        worldDir = Quaternion.AngleAxis(Random.Range(-9f, 9f), camT.right) * worldDir;

        var s = new Shaft { originLocal = statue.InverseTransformPoint(pos), normal = nrm,
                            dirLocal = statue.InverseTransformDirection(worldDir),
                            bornAt = Time.unscaledTime, phase = Random.Range(0f, 10f) };
        // Per-shaft width variation. Identical shafts are a tell; nothing in
        // nature emits a matched set.
        s.widthScale = Random.Range(0.65f, 1.35f);

        var lgo = new GameObject("Glow");
        lgo.transform.SetParent(root.transform, false);
        s.glow = lgo.AddComponent<Light>();
        s.glow.type = LightType.Point;
        s.glow.color = lightColor;
        s.glow.range = 8f;
        s.glow.intensity = 0f;
        s.glow.shadows = LightShadows.None;

        var rgo = new GameObject("Ray");
        rgo.transform.SetParent(root.transform, false);
        s.line = rgo.AddComponent<LineRenderer>();
        s.line.useWorldSpace = true;
        s.line.positionCount = 2;
        s.line.material = rayMat;
        s.line.numCapVertices = 3;
        s.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        s.line.receiveShadows = false;
        // Narrow at the stone, swelling toward the lens, then tapering — a shaft
        // of even width is the shape of a ribbon, not of light.
        s.line.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.12f), new Keyframe(0.55f, 1f), new Keyframe(1f, 0.55f));
        s.line.widthMultiplier = 0f;

        // Also on the rig, not on the shaft: the shaft root is a child of the
        // statue and would scale these by 3.2 the same way. Its position is
        // driven every frame in UpdateShafts anyway.
        s.motes = MakeParticles("Motes", transform, coreColor);
        var mm = s.motes.main;
        mm.startLifetime = 2.4f;
        mm.startSpeed = 1.6f;
        mm.startSize = 0.055f;
        mm.gravityModifier = -0.02f;      // drift upward, the way lit dust does
        mm.maxParticles = Mathf.Max(4, Scaled(motesPerRay) * 2);
        var ms = s.motes.shape;
        ms.shapeType = ParticleSystemShapeType.Cone;
        ms.angle = 7f;
        ms.radius = 0.06f;
        s.motes.Play();

        return s;
    }

    private void UpdateShafts(float t)
    {
        float camDist = Vector3.Distance(camT.position, center);
        // Eases the shafts back as the lens arrives rather than switching them
        // down, so the change is never a visible step.
        float proximity = Mathf.Clamp(camDist / Mathf.Max(1f, startDistance), nearShaftFalloff, 1f);

        // The shafts come out of cracks in a statue that is about to stop
        // existing. Once it goes they have no source, so they hand over to the
        // lens flare rather than hanging in the air pouring out of nothing.
        float handover = burstStartedAt < 0f
            ? 1f
            : Mathf.Clamp01(1f - (t - burstStartedAt - 0.35f) / 0.9f);

        for (int i = 0; i < shafts.Count; i++)
        {
            Shaft s = shafts[i];
            float age = Time.unscaledTime - s.bornAt;

            // Punch out with overshoot, then settle. Light forcing its way
            // through stone arrives; it does not fade up like a dimmer.
            float grow = 1f - Mathf.Exp(-age * 5f);
            grow *= 1f + 0.22f * Mathf.Exp(-age * 6f) * Mathf.Sin(age * 26f);
            grow = Mathf.Clamp01(grow);

            // A slow, shallow breath. Enough that the light is not dead, nowhere
            // near enough to strobe.
            float flick = 1f + (Mathf.PerlinNoise(s.phase, t * flickerSpeed) - 0.5f) * 2f * rayFlicker;

            s.glow.intensity = lightIntensity * grow * (0.25f + 0.75f * tension) * flick * handover;
            s.glow.color = Color.Lerp(lightColor, coreColor, rayHeat * tension);

            Vector3 origin = s.Origin(statue);
            // Direction was decided when the crack opened and does not change.
            // The sweep across frame comes from the camera travelling past a
            // stationary shaft, which is how it works in life and the only way it
            // stops looking like a lighting rig.
            Vector3 dir = statue.TransformDirection(s.dirLocal).normalized;
            // ==== THE FILL HAS TO STAY BOUNDED AS THE LENS CLOSES ====
            //
            // A shaft is an additive quad 24 metres long. That is a reasonable
            // amount of screen at the fifteen metres the push STARTS at, and an
            // absurd one at the five it ends at — the same geometry covers about
            // nine times the pixels. Ten of them, over a couple of hundred soft
            // dust billboards, over a full-screen fog raymarch: the shot gets
            // more expensive the closer it gets, which is exactly the shape of
            // "it lags at the end".
            //
            // Capping the length against the camera's own distance holds the
            // coverage roughly constant across the push. It also reads better:
            // walking up to a beam of light, you see LESS of its length, not
            // more, and ten shafts at full opacity from five metres blow out to
            // white — which the note on rayHeat above already warns about.
            float len = Mathf.Min(rayLength, Mathf.Max(3f, camDist * shaftLengthPerMetre)) * grow;

            // Occlusion. A shaft that passes through a wall destroys the shot
            // faster than any amount of shader quality can save it.
            if (Physics.Raycast(origin + dir * 0.15f, dir, out RaycastHit blk, len, ~0, QueryTriggerInteraction.Ignore)
                && !blk.transform.IsChildOf(statue))
                len = Mathf.Max(0.4f, blk.distance);

            s.line.SetPosition(0, origin);
            s.line.SetPosition(1, origin + dir * len);
            // Intensity breathes; WIDTH does not. A shaft whose thickness pulses
            // reads as a bad effect rather than as light.
            s.line.widthMultiplier = rayWidth * s.widthScale * grow * (0.6f + 0.6f * tension) * handover * proximity;

            // Hot only at the mouth, and only a little. The far end stays the deep
            // ember, so a shaft reads as light escaping from something burning
            // rather than as a white bar drawn across the frame.
            Color mouth = Color.Lerp(lightColor, coreColor, rayHeat * (0.5f + 0.5f * tension));
            mouth.a = grow * rayOpacity * (0.45f + 0.55f * tension) * handover * proximity;
            s.line.startColor = mouth;

            Color tail = lightColor; tail.a = 0f;
            s.line.endColor = tail;

            // Motes ride the shaft. This is the single biggest contributor to a
            // shaft looking volumetric instead of printed on the screen.
            if (s.motes != null)
            {
                s.motes.transform.position = origin;
                s.motes.transform.rotation = Quaternion.LookRotation(dir);
                var em = s.motes.emission;
                em.rateOverTime = Scaled(motesPerRay) * grow * (0.25f + tension) * handover;
            }
        }
    }

    // ======================= burst =======================

    private IEnumerator BurstRoutine()
    {
        var polish = TrailerCinematicPolish.GetOrCreate();

        if (AudioManager.Instance != null && !string.IsNullOrEmpty(burstSound))
            AudioManager.Instance.PlaySFX3D(burstSound, center);

        // Riser first: the ear needs a moment of rising pitch BEFORE the hit, or
        // the burst lands as a bang rather than as an arrival.
        if (AudioManager.Instance != null)
        {
            if (!string.IsNullOrEmpty(riserSound)) AudioManager.Instance.PlaySFX3D(riserSound, center);
            if (!string.IsNullOrEmpty(rubbleSound)) AudioManager.Instance.PlaySFX3D(rubbleSound, center);
        }

        if (flashes) polish.ImpactPunch(1f, 0.7f);
        polish.TimeRamp(0.32f, pierceDuration * 0.6f, 0.04f, 0.45f);
        Kick(center, 2.4f);

        // Widen every fracture at once — the stone gives up as one.
        for (int i = 0; i < cracks.Count; i++)
            if (cracks[i] != null) cracks[i].SetHeat(1f, crackWidthScale * 3.2f);

        SpawnDebris();
        BuildPierce();
        if (chipBurst != null) chipBurst.Emit(120);

        // Hide the mesh under the flare. A single mesh cannot really shatter, so
        // the swap happens at the brightest frame, where the eye cannot follow it.
        if (vanishOnBurst)
        {
            yield return new WaitForSecondsRealtime(0.10f);
            for (int i = 0; i < statueRenderers.Count; i++)
                if (statueRenderers[i] != null) statueRenderers[i].enabled = false;
            if (sheetDust != null)
            {
                var em = sheetDust.emission;
                em.rateOverTime = 400f;
            }
        }

        yield return new WaitForSecondsRealtime(0.5f);
        if (sheetDust != null)
        {
            var em = sheetDust.emission;
            em.rateOverTime = 60f;     // let it hang and settle in the light
        }
    }

    // ===================== the ending =====================
    //
    // The last fracture takes the lens.
    //
    // Everywhere else in this shot a shaft aimed at the camera would be wrong —
    // head-on it foreshortens to a dot. Here that is the point: pointed at the
    // lens it stops being a shaft and becomes a flood that owns the frame. Ember
    // to white to black, and the blackout IS the cut to the next shot.
    //
    // Drawn through TrailerCinematicPolish's overlay rather than as geometry. Two
    // earlier attempts — a screen-space Canvas of its own, then quads parented to
    // the camera — both rendered nothing, and the cost of chasing invisible
    // geometry a third time is worse than the cost of reusing the Image that
    // already, visibly, draws the letterbox fades every shot.
    private Vector3 pierceOrigin;

    private void BuildPierce()
    {
        // Open from the crack nearest the middle of frame: the flood has to grow
        // out of somewhere the audience is already looking, or it reads as an
        // unrelated wipe rather than as this light arriving.
        pierceOrigin = center;
        float best = float.MaxValue;
        for (int i = 0; i < shafts.Count; i++)
        {
            Vector3 p = shafts[i].Origin(statue);
            Vector3 v = shotCamera.WorldToViewportPoint(p);
            if (v.z <= 0f) continue;
            float d = (new Vector2(v.x, v.y) - new Vector2(0.5f, 0.5f)).sqrMagnitude;
            if (d < best) { best = d; pierceOrigin = p; }
        }
    }

    private void UpdatePierce(float t)
    {
        if (burstStartedAt < 0f) return;

        float u = Mathf.Clamp01((t - burstStartedAt) / Mathf.Max(0.05f, pierceDuration));

        Color c;
        if (!flashes)
        {
            // The shutter alone. It starts the moment the stone gives, so the
            // closing bars ARE the last beat rather than something that happens
            // after a flash — and there is nothing full-screen and additive on
            // the frames that can least afford it.
            if (!closing)
            {
                closing = true;
                TrailerCinematicPolish.GetOrCreate().CloseBars(pierceDuration * 0.55f);
            }
            // Black comes up behind the bars only at the very end, so the last
            // thing visible through the closing gap is the statue, not a colour.
            c = Color.black;
            c.a = Mathf.Clamp01((u - 0.70f) / 0.30f);
            TrailerCinematicPolish.GetOrCreate().SetFlash(c);
            return;
        }

        if (u < 0.62f)
        {
            // Ember rising. Accelerating, because light forcing its way through
            // stone does not open at a constant rate — it gives way.
            c = Color.Lerp(lightColor, coreColor, u / 0.62f);
            c.a = Mathf.Clamp01((u / 0.62f) * (u / 0.62f)) * 0.95f;
        }
        else if (u < 0.80f)
        {
            c = Color.Lerp(coreColor, Color.white, (u - 0.62f) / 0.18f);
            c.a = 1f;
        }
        else
        {
            // ==== THE TRANSITION ====
            //
            // This used to lerp the white to black and stop. That is not a
            // transition, it is the picture being switched off: nothing moves
            // across the join, so there is no frame for an editor to cut ON and
            // the shot simply runs out.
            //
            // Instead the LETTERBOX slams shut over the blowout. The bars have
            // framed every frame of this shot from the first, so the close
            // introduces nothing new at the last second; it is the shot shutting
            // its own eye, and it gives an editor a moving frame to cut on.
            //
            // The flash HOLDS while the bars come together, and only goes black
            // behind them at the very end. It deliberately does not clear: the
            // statue is one unfractured mesh and never actually breaks apart, so
            // a frame of aftermath would show it standing there intact — which is
            // the same reason vanishOnBurst is off. The motion across the join is
            // the shutter, not the picture.
            float k = Mathf.Clamp01((u - 0.80f) / 0.15f);
            c = Color.Lerp(Color.white, Color.black, k * k);
            c.a = 1f;
            if (!closing)
            {
                closing = true;
                TrailerCinematicPolish.GetOrCreate().CloseBars(pierceDuration * 0.20f);
            }
        }

        TrailerCinematicPolish.GetOrCreate().SetFlash(c);
    }

    private bool closing;

    // ==== THE STONE BREAKING INTO PIECES IS A DISK READ ====
    //
    // SpawnDebris used to Instantiate twenty-odd rocks on the single frame the
    // statue bursts. Those six LProck models, their materials and their textures
    // have not been touched at any earlier point in the shot, so that frame is
    // where Unity goes and LOADS them — synchronously, while the time ramp is
    // running and the burst coroutine is doing everything else it does. One
    // frame, all of it, exactly on the beat the audience is looking at.
    //
    // So they are built during the establish instead: two per frame through the
    // two and a half seconds of deliberate silence at the top of the shot, where
    // nothing is happening and a hitch costs nothing. They sit inactive off to
    // one side until the burst, which then only has to place them and push.
    private readonly List<GameObject> debrisPool = new List<GameObject>();

    private IEnumerator WarmDebris()
    {
        if (debrisPrefabs == null || debrisPrefabs.Length == 0 || debrisCount <= 0) yield break;

        int want = Scaled(debrisCount);
        for (int i = 0; i < want; i++)
        {
            GameObject prefab = debrisPrefabs[Random.Range(0, debrisPrefabs.Length)];
            if (prefab == null) continue;

            GameObject chunk = Instantiate(prefab, transform);
            chunk.name = "Debris_" + i;

            debrisPool.Add(chunk);
            debrisScale.Add(chunk.transform.localScale * Random.Range(0.07f, 0.26f));
            PrepareChunk(chunk);
            Park(chunk);

            // Two a frame. Spreading the load is the whole point; doing them all
            // here would only move the same hitch to the top of the shot.
            if ((i & 1) == 1) yield return null;
        }
    }

    // ==== LOADING A MESH IS NOT THE SAME AS HAVING DRAWN IT ====
    //
    // Building the chunks early moved the asset load off the burst frame, and
    // the burst still locked the editor solid. Instantiating a prefab reads its
    // mesh, its material and its textures — it does NOT compile the shader.
    // Under URP a material needs a compiled variant per pass and per keyword set
    // it is actually rendered with, and Unity compiles those the first time the
    // thing is DRAWN, synchronously, on the render thread. Six rock materials
    // times forward, shadow caster, depth and depth-normals, times the fog and
    // light keywords in play, is a lot of variants to compile in one frame — and
    // a synchronous compile is not a dropped frame, it is the editor stopping.
    //
    // Parked chunks used to be inactive, so their first draw was the burst. Now
    // they stay ACTIVE from the establish onward, kinematic and shrunk to
    // effectively nothing at the statue's centre: submitted for drawing every
    // frame, and occupying no pixels worth speaking of. Every variant the burst
    // will need is compiled during the silence at the top of the shot, where
    // there is nothing on screen for a stall to interrupt.
    private const float ParkedScale = 0.004f;
    private readonly List<Vector3> debrisScale = new List<Vector3>();

    private void Park(GameObject chunk)
    {
        chunk.transform.position = center;
        chunk.transform.localScale = debrisScale[debrisPool.Count - 1] * ParkedScale;

        var rb = chunk.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;          // it must not fall out of the stone
    }

    private void SpawnDebris()
    {
        // Everything that costs anything was done during the establish. All this
        // frame does is move twenty transforms and set twenty velocities.
        for (int i = 0; i < debrisPool.Count; i++)
        {
            GameObject chunk = debrisPool[i];
            if (chunk == null) continue;

            Vector3 from = center;
            if (cracks.Count > 0)
            {
                var pick = cracks[Random.Range(0, cracks.Count)];
                if (pick != null) from = pick.Tip;
            }

            chunk.transform.SetPositionAndRotation(from, Random.rotation);
            chunk.transform.localScale = debrisScale[i];

            var rb = chunk.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                Vector3 away = (from - center).normalized + Vector3.up * Random.Range(0.3f, 0.9f);
                rb.linearVelocity = away * Random.Range(4f, 9f) + Random.insideUnitSphere * 1.5f;
                rb.angularVelocity = Random.insideUnitSphere * 8f;
            }

            debris.Add(chunk);
            Destroy(chunk, 7f);
        }
        debrisPool.Clear();
        debrisScale.Clear();
    }

    // Collider, body and collision filtering, all done off the beat.
    private void PrepareChunk(GameObject chunk)
    {
        // ==== A SPHERE, NOT A COOKED CONVEX HULL ====
        //
        // This used to add a MeshCollider and set convex on the rock's own mesh.
        // The LProck meshes are imported with Read/Write OFF, so PhysX cannot
        // cook a hull from them at all: every chunk logged an error and ended up
        // with NO collider, falling through the world — the opposite of what the
        // code was trying to buy. And cooking twenty-odd hulls costs real time,
        // which used to be spent on the burst frame.
        //
        // A sphere off the mesh's local bounds tumbles and settles convincingly
        // for the second of screen time any of this gets, and needs no readable
        // mesh. Mesh.bounds is metadata, available whatever the import settings.
        if (chunk.GetComponentInChildren<Collider>() == null)
        {
            var mf = chunk.GetComponentInChildren<MeshFilter>();
            var host = mf != null ? mf.gameObject : chunk;
            var sc = host.AddComponent<SphereCollider>();
            if (mf != null && mf.sharedMesh != null)
            {
                sc.center = mf.sharedMesh.bounds.center;
                sc.radius = Mathf.Max(0.02f, mf.sharedMesh.bounds.extents.magnitude * 0.6f);
            }
        }

        // They are launched FROM the crack tips, which are ON the statue's
        // collider. Without this every chunk starts deeply interpenetrating a
        // concave mesh and PhysX spends the burst frame pushing them out of it.
        IgnoreStatue(chunk);

        // NOT '??'. The null-coalescing operator compares against real null and
        // bypasses UnityEngine.Object's == overload, so a destroyed or absent
        // component slips through as "not null" and the next line throws.
        var rb = chunk.GetComponent<Rigidbody>();
        if (rb == null) rb = chunk.AddComponent<Rigidbody>();
        rb.mass = 0.4f;
    }

    private readonly List<GameObject> debris = new List<GameObject>();

    private void IgnoreStatue(GameObject chunk)
    {
        if (statueColliders == null)
        {
            statueColliders = statue.GetComponentsInChildren<Collider>(true);
        }
        if (statueColliders.Length == 0) return;

        foreach (var mine in chunk.GetComponentsInChildren<Collider>(true))
        {
            if (mine == null) continue;
            foreach (var theirs in statueColliders)
                if (theirs != null) Physics.IgnoreCollision(mine, theirs, true);
        }
    }

    private Collider[] statueColliders;

    // ==== THE SHOT HAS TO STOP COSTING SOMETHING WHEN IT ENDS ====
    //
    // Nothing here used to be switched off. The screen went black on the pierce
    // and the coroutine returned, and behind that black the shot carried on
    // burning: ten realtime point lights, thirty view-aligned line renderers
    // rebuilding their meshes every frame on a statue that never stops
    // trembling, a dozen particle systems still emitting, and the debris still
    // being simulated. You cannot see any of it, so the only symptom is that the
    // editor never recovers — which reads as a freeze that starts when the
    // statue breaks and never goes away.
    private void TearDown()
    {
        for (int i = 0; i < shafts.Count; i++)
        {
            Shaft s = shafts[i];
            if (s == null) continue;
            if (s.glow != null) Destroy(s.glow.gameObject);
            if (s.line != null) Destroy(s.line.gameObject);
            if (s.motes != null) { s.motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); Destroy(s.motes.gameObject); }
        }
        shafts.Clear();

        for (int i = 0; i < cracks.Count; i++)
            if (cracks[i] != null) Destroy(cracks[i].gameObject);
        cracks.Clear();

        if (sheetDust != null) { sheetDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); Destroy(sheetDust.gameObject); }
        if (chipBurst != null) { chipBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); Destroy(chipBurst.gameObject); }

        for (int i = 0; i < debris.Count; i++)
            if (debris[i] != null) Destroy(debris[i]);
        debris.Clear();

        // Anything the burst never got to use — the shot can be cut short.
        for (int i = 0; i < debrisPool.Count; i++)
            if (debrisPool[i] != null) Destroy(debrisPool[i]);
        debrisPool.Clear();
        debrisScale.Clear();

        // Put the stone back where it was found and let it be static again.
        if (statue != null) statue.position = statueHome;
        if (tremorBodyIsOurs && tremorBody != null) { Destroy(tremorBody); tremorBody = null; tremorBodyIsOurs = false; }
    }

    private void OnDestroy()
    {
        if (rayMat != null) Destroy(rayMat);
        if (crackMat != null) Destroy(crackMat);
        if (tremorBodyIsOurs && tremorBody != null) Destroy(tremorBody);
    }
}
