using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// The army-march episode: three shots, a night storm, and a thrown axe.
//
// ==== WHAT THIS REPLACED, AND WHY ====
//
// The first version was one camera doing one move for eight seconds: it tilted
// up, then crept forward at a metre a second while three hundred skeletons
// walked at it. Everything was visible in the first frame - the field, the
// army, the boss - so there was nothing to discover and nowhere for it to
// build. That is a technical demonstration, not a trailer beat.
//
// It is now three shots with one job each:
//
//   GROUND    You are lying in the grass. Fog, dark, nothing. A boot comes
//             down and the camera jolts. Another. You hear the legion before
//             you are allowed to see it.
//   LEGION    Hard cut to high and far. A dark mass in the fog resolves as the
//             camera comes down and in: rank after rank walks OUT of the fog
//             toward you, and the column behind them never ends.
//   CHALLENGE Cut to a low three-quarter. The army grinds to a halt behind
//             him. A beat of nothing. He throws the axe at the lens.
//
// Withhold, reveal, threaten. The assets did not change; the order they are
// shown in did.
//
// Three structural decisions are worth knowing before editing this:
//
// EVERY CAMERA POSITION IS RELATIVE TO THE BOSS, never absolute. He is walking
// the whole time, so an absolute path drifts out of composition the moment
// marchSpeed or a shot duration is touched, and every shot has to be re-framed
// by hand. In his frame, "nine metres in front of him, chest height" stays
// nine metres in front of him at chest height forever.
//
// THE THROW IS NOT ON ITS OWN CLOCK. It fires when the last shot ends.
// Previously the shot timing and timeUntilThrow were two independent numbers
// that had to be kept in agreement by hand, and an edit to either silently
// desynchronised the other.
//
// AND THE COLUMN IS BUILT TO BE UNCOUNTABLE. See SpawnLegion.
public class TrailerLegionDirector : MonoBehaviour
{
    // A single camera setup. Offsets are in the boss's own frame: +Z is the
    // direction he is marching (so positive Z is IN FRONT of him, which is
    // where the camera lives), +X is his right, +Y is height above the ground
    // for the camera and above his feet for the look target.
    [System.Serializable]
    public class Shot
    {
        public string label = "shot";
        public float duration = 4f;

        [Header("Camera path (boss-relative)")]
        public Vector3 startOffset = new Vector3(0f, 2f, 14f);
        public Vector3 endOffset = new Vector3(0f, 2f, 12f);
        [Tooltip("What the camera points at, relative to the boss's feet.")]
        public Vector3 lookOffset = new Vector3(0f, 2.4f, 0f);

        [Header("Lens")]
        public float startFOV = 45f;
        public float endFOV = 45f;
        [Tooltip("Dutch angle in degrees. A couple of degrees is unease; ten is a music video.")]
        public float roll = 0f;

        [Header("Feel")]
        [Tooltip("Handheld drift amplitude in metres. 0 is a locked-off tripod.")]
        public float handheld = 0.1f;
        [Tooltip("How much of the boss's footfall shake this shot takes. The ground shot wants all of it; the wide reveal wants almost none, because a wide shot that trembles reads as a fault rather than as weight.")]
        [Range(0f, 3f)] public float shakeResponse = 1f;

        public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }

    [Header("Cast")]
    public Transform mainCamera;
    public Transform bossTransform;
    public Animator bossAnimator;
    public Transform bossWeapon;

    [Header("Shots")]
    [Tooltip("Played in order, with a hard cut between each. Leave empty and the three authored shots are built at startup.")]
    public Shot[] shots;

    [Header("The legion")]
    public GameObject skeletonPrefab;
    public int skeletonCount = 300;
    [Tooltip("How many stand shoulder to shoulder in the front rank. Every rank behind it is a little wider.")]
    public int filesPerRank = 11;
    public float fileSpacing = 1.7f;
    public float rankSpacing = 1.9f;
    [Tooltip("Extra files added per rank going back, which turns the block into a wedge with the boss at its point. Keep it small or the column runs out of soldiers before it runs out of depth.")]
    public float wedgeWidening = 0.45f;
    [Tooltip("How much further apart each successive rank sits. This is what makes the column uncountable - see SpawnLegion.")]
    [Range(0f, 0.3f)] public float depthStretch = 0.07f;
    [Tooltip("Random slop on each soldier's place in the rank. Zero is a parade; too much is a mob. About a quarter of the spacing reads as an army that has been walking for days.")]
    public float positionJitter = 0.45f;
    public float yawJitter = 7f;
    public Vector2 scaleJitter = new Vector2(0.93f, 1.07f);
    [Tooltip("Per-soldier march speed variation, as a fraction. Without it the whole formation moves like one object.")]
    [Range(0f, 0.25f)] public float speedJitter = 0.05f;
    public float marchSpeed = 2f;
    [Tooltip("Where the front rank sits relative to the boss. Negative is behind him.")]
    public float frontRankOffset = -3.5f;

    [Header("Night storm rig")]
    [Tooltip("Apply the lighting below at startup. The scene as authored lit everything with flat skybox ambient at full strength and a white directional at 0.14, which is why the army had no silhouette and no form.")]
    public bool applyLightingRig = true;
    public Light mainDirectionalLight;
    [Tooltip("The moon. Cold and weak - it is not there to light the army, it is there to put an edge on it.")]
    public Color mainLightColor = new Color(0.46f, 0.58f, 0.85f);
    public float mainLightIntensity = 0.9f;
    [Tooltip("How high the moon sits. Low is what rims a silhouette; high is what flattens one.")]
    [Range(2f, 45f)] public float moonElevation = 11f;
    [Tooltip("Swing of the moon off dead-behind-the-army, in degrees. Straight behind is a clean rim; a little to the side gives the ranks some separation.")]
    public float moonAzimuthOffset = 28f;
    public Color ambientSky = new Color(0.10f, 0.13f, 0.20f);
    public Color ambientEquator = new Color(0.07f, 0.08f, 0.12f);
    public Color ambientGround = new Color(0.03f, 0.03f, 0.04f);
    [Tooltip("The volumetric fog is set to Follow Scene Fog Colour, so this is what the fog is made of. Nothing in the scene was writing it, which left the fog on whatever colour the last scene happened to leave behind.")]
    public Color fogTint = new Color(0.30f, 0.36f, 0.46f);

    [Header("Lightning")]
    [Tooltip("Seconds from the start of the episode. Each entry is one strike.")]
    public float[] lightningTimes = { 1.7f, 5.4f, 9.2f, 12.1f };
    public Color lightningColor = new Color(0.82f, 0.88f, 1f);
    public float lightningIntensity = 14f;
    [Tooltip("Gap between the flash and the thunder - distance, in other words. It shortens through the episode on its own, so the storm reads as closing in.")]
    public float thunderDelay = 0.9f;

    [Header("Footfalls")]
    public float stepShakeIntensity = 0.3f;
    [Tooltip("Seconds between the boss's steps, scaled by march speed.")]
    public float stepFrequency = 1.5f;
    [Tooltip("Optional puff spawned under the boss on each footfall.")]
    public GameObject stepDustPrefab;
    [Tooltip("Optional dust the marching army drags with it. Parented to the boss and left running.")]
    public GameObject marchDustPrefab;

    [Header("Audio")]
    [Tooltip("Play the trailer score and the march bed. Every event is guarded, so a project with these unassigned is silent rather than broken.")]
    public bool playAudio = true;

    [Header("The throw")]
    public float animationWindupTime = 0.8f;
    public float weaponFlightDuration = 0.4f;
    public float weaponArcHeight = 1.5f;
    [Tooltip("How far BEHIND the lens the weapon is aimed. Aimed in front of it, the axe decelerates to a stop just short of the glass and you watch it park in mid-air.")]
    public float throwOvershoot = 1.6f;
    [Tooltip("How much bigger the weapon gets by the time it reaches the lens.")]
    public float impactScaleMultiplier = 4.2f;
    public float throwSpins = 2.25f;
    public AnimationCurve weaponFlightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public AnimationCurve zoomCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public float cinematicZoomFOV = 40f;

    [Header("Impact transition")]
    [Tooltip("Smash to black on contact and hold, which is a cut the next episode can start from.")]
    public bool smashToBlack = true;
    public Color impactFadeColor = Color.black;
    public float impactFadeDuration = 0.14f;
    public float holdBlackDuration = 0.35f;
    public UnityEngine.Events.UnityEvent onEpisodeFinished;

    // ---- runtime ------------------------------------------------------------

    private class Soldier
    {
        public Transform t;
        public Animator anim;
        public float speed;
        public int snapPhase;      // which frame in the stagger this one grounds on
    }

    private readonly List<Soldier> legion = new List<Soldier>(320);

    private bool isBossMarching = true;
    private bool isArmyMarching = true;
    private bool cameraDriven = true;

    private int shotIndex;
    private float shotElapsed;
    private float episodeElapsed;
    private float stepTimer;
    private float stepImpulse;
    private float driftSeed;
    private int nextLightning;

    private Camera cam;
    private float rigIntensity;
    private Color rigColor;
    private int windHandle = -1, marchHandle = -1;

    private void Start()
    {
        cam = mainCamera != null ? mainCamera.GetComponent<Camera>() : null;
        if (shots == null || shots.Length == 0) shots = BuildDefaultShots();

        ApplyLightingRig();
        SpawnLegion();
        SpawnMarchDust();
        StartAudioBed();

        driftSeed = Random.Range(0f, 100f);
        // Frame the first shot before anything renders, so the episode does not
        // open on one frame of wherever the camera happened to be parked.
        DriveCamera(0f, true);

        StartCoroutine(CinematicRoutine());
    }

    private void OnDisable()
    {
        // Every beat below plays with Time.timeScale. Leaving the scene, or
        // stopping play, part-way through one used to leave the game in slow
        // motion.
        if (Time.timeScale < 0.999f) Time.timeScale = 1f;
        StopAudioBed();
    }

    // ======================================================================
    //  The three shots
    // ======================================================================

    // Authored in code rather than left to the inspector so the episode has a
    // known-good starting point in any scene, and so the intent of each shot is
    // written down next to its numbers.
    private Shot[] BuildDefaultShots()
    {
        var ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        // GROUND. Down in the grass on a long lens, so depth compresses and he
        // fills the frame sooner than the distance says he should. The camera
        // barely moves: it is the boss walking INTO the shot that gives it
        // life, and every footfall goes straight through the lens.
        var ground = new Shot
        {
            label = "1 - Ground",
            duration = 3.2f,
            startOffset = new Vector3(1.2f, 0.32f, 15f),
            endOffset = new Vector3(0.9f, 0.40f, 13.2f),
            lookOffset = new Vector3(0f, 0.55f, 0f),
            startFOV = 34f,
            endFOV = 32f,
            roll = -1.5f,
            handheld = 0.06f,
            shakeResponse = 2.4f,
            ease = ease,
        };

        // LEGION. The reveal, and the only wide lens in the episode.
        //
        // It starts FAR - far enough that the army is a darkness in the fog
        // rather than a set of models - and comes down and IN. Closing is the
        // whole point: fog is a function of distance from the lens, so as the
        // camera closes, rank after rank crosses out of the fog toward the
        // viewer while the ranks behind them stay lost in it. The army appears
        // to be arriving out of nothing, endlessly, which is the impression
        // three hundred models could never make by being counted.
        //
        // Almost no shake: a wide shot that trembles reads as a fault.
        var legionShot = new Shot
        {
            label = "2 - Legion",
            duration = 5.8f,
            startOffset = new Vector3(-5f, 15.5f, 54f),
            endOffset = new Vector3(3f, 6.2f, 25f),
            lookOffset = new Vector3(0f, 1.6f, -14f),
            startFOV = 56f,
            endFOV = 47f,
            roll = 0f,
            handheld = 0.14f,
            shakeResponse = 0.25f,
            ease = ease,
        };

        // CHALLENGE. Back in close on a three-quarter, slightly below his eye
        // line so he stands over the lens. Pushing in very slowly through the
        // halt, which is the shot holding its breath.
        var challenge = new Shot
        {
            label = "3 - Challenge",
            duration = 4.4f,
            startOffset = new Vector3(3.6f, 1.55f, 9.5f),
            endOffset = new Vector3(2.8f, 1.5f, 7.6f),
            lookOffset = new Vector3(0f, 2.7f, 0f),
            startFOV = 46f,
            endFOV = 42f,
            roll = 2f,
            handheld = 0.09f,
            shakeResponse = 1.2f,
            ease = ease,
        };

        return new[] { ground, legionShot, challenge };
    }

    // The boss's frame. +Z is where he is going, +X is his right.
    private void BossAxes(out Vector3 fwd, out Vector3 right)
    {
        fwd = bossTransform != null ? bossTransform.forward : Vector3.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        fwd.Normalize();
        right = new Vector3(fwd.z, 0f, -fwd.x);
    }

    // A camera position: grounded, then lifted by the offset's Y. Grounding it
    // is what keeps a low shot low over a rolling field instead of burying the
    // lens on the next rise.
    private Vector3 CameraPoint(Vector3 o)
    {
        BossAxes(out Vector3 fwd, out Vector3 right);
        Vector3 p = bossTransform.position + fwd * o.z + right * o.x;
        p.y = GetTerrainHeight(p) + o.y;
        return p;
    }

    // A look target: relative to the boss's feet, NOT to the terrain, so it
    // stays locked to him rather than sliding up and down the landscape.
    private Vector3 LookPoint(Vector3 o)
    {
        BossAxes(out Vector3 fwd, out Vector3 right);
        Vector3 p = bossTransform.position + fwd * o.z + right * o.x;
        p.y = bossTransform.position.y + o.y;
        return p;
    }

    private void DriveCamera(float dt, bool snap = false)
    {
        if (mainCamera == null || bossTransform == null || shots == null || shots.Length == 0) return;
        if (shotIndex >= shots.Length) shotIndex = shots.Length - 1;

        Shot s = shots[shotIndex];
        float u = s.duration > 0.001f ? Mathf.Clamp01(shotElapsed / s.duration) : 1f;
        float k = s.ease != null ? s.ease.Evaluate(u) : u;

        Vector3 basePos = CameraPoint(Vector3.LerpUnclamped(s.startOffset, s.endOffset, k));
        Vector3 look = LookPoint(s.lookOffset);

        // Handheld: two different frequencies so it wanders like a held camera
        // rather than tracing the same diagonal back and forth.
        float tt = episodeElapsed;
        Vector3 drift = new Vector3(
            Mathf.PerlinNoise(driftSeed + tt * 0.55f, 11.3f) - 0.5f,
            Mathf.PerlinNoise(37.7f, driftSeed + tt * 0.83f) - 0.5f,
            0f) * (s.handheld * 2f);

        // The footfall drops the camera and it recovers. See stepImpulse.
        Vector3 shake = new Vector3(0f, -stepImpulse * s.shakeResponse, 0f);

        Vector3 want = basePos + drift + shake;
        // A cut is instant; within a shot the camera is smoothed a little so
        // the terrain grounding does not read as a stair-step.
        mainCamera.position = snap ? want : Vector3.Lerp(mainCamera.position, want, 1f - Mathf.Exp(-12f * dt));

        Quaternion aim = Quaternion.LookRotation((look - mainCamera.position).normalized, Vector3.up);
        if (Mathf.Abs(s.roll) > 0.001f) aim *= Quaternion.Euler(0f, 0f, s.roll);
        mainCamera.rotation = snap ? aim : Quaternion.Slerp(mainCamera.rotation, aim, 1f - Mathf.Exp(-14f * dt));

        if (cam != null) cam.fieldOfView = Mathf.LerpUnclamped(s.startFOV, s.endFOV, k);
    }

    private void CutTo(int index)
    {
        shotIndex = Mathf.Clamp(index, 0, shots.Length - 1);
        shotElapsed = 0f;
        // A fresh drift phase, so the new shot does not inherit the old one's
        // wobble mid-stroke and make the cut look like a jump.
        driftSeed = Random.Range(0f, 100f);
        DriveCamera(0f, true);

        if (shotIndex == 1) Sfx(AudioID.Trailer_WarHorn);
    }

    // ======================================================================
    //  The legion
    // ======================================================================

    // ==== AN ARMY YOU CAN COUNT IS NOT AN ARMY ====
    //
    // They used to be scattered at random through a 40 x 18 metre box. At three
    // hundred that is better than two per five square metres, so they walked
    // through each other, and randomness gives clumps and holes rather than a
    // line. Every one then moved at exactly the same speed in exactly the same
    // direction, which is what made it read as one object sliding forward
    // rather than as men walking. Worst of all, the whole thing FITTED ON
    // SCREEN: you could see where it ended, so you knew how many there were,
    // and three hundred is not very many.
    //
    // Ranks and files now, widening behind the boss into a wedge with him at
    // its point. Two things make it uncountable:
    //
    // DEPTH STRETCH. Each successive rank sits a little further behind the one
    // in front than that one did. The spacing compounds, so the same three
    // hundred soldiers cover well over a hundred metres of depth instead of
    // thirty - and the far ranks, which are the sparse ones, are exactly the
    // ranks the volumetric fog has already swallowed. You cannot see that they
    // are thinning out, only that the column keeps going.
    //
    // AND THE CAMERA CLOSES ON THEM. Fog is a function of distance from the
    // lens, so shot 2 walking the camera in turns the fog into a moving
    // curtain: rank after rank steps out of it toward the viewer while the
    // ranks behind stay hidden. An army that keeps arriving out of nothing
    // reads as far larger than one you were shown all of.
    //
    // A rank's walk cycle also starts a little later than the rank in front,
    // so the step travels backwards through the formation as a ripple. It is
    // the cheapest thing in this file and it is most of what sells it.
    private void SpawnLegion()
    {
        legion.Clear();
        if (skeletonPrefab == null || bossTransform == null || skeletonCount <= 0) return;

        BossAxes(out Vector3 fwd, out Vector3 right);

        int placed = 0;
        int rank = 0;
        float rowZ = frontRankOffset;

        while (placed < skeletonCount && rank < 400)
        {
            int files = Mathf.Max(2, Mathf.RoundToInt(filesPerRank + rank * wedgeWidening));
            float halfSpan = (files - 1) * 0.5f;

            for (int f = 0; f < files && placed < skeletonCount; f++, placed++)
            {
                float x = (f - halfSpan) * fileSpacing
                        + Random.Range(-positionJitter, positionJitter);
                float z = rowZ + Random.Range(-positionJitter, positionJitter);

                Vector3 pos = bossTransform.position + fwd * z + right * x;
                pos.y = GetTerrainHeight(pos);

                Quaternion rot = bossTransform.rotation * Quaternion.Euler(0f, Random.Range(-yawJitter, yawJitter), 0f);
                GameObject go = Instantiate(skeletonPrefab, pos, rot);

                float sc = Random.Range(scaleJitter.x, scaleJitter.y);
                go.transform.localScale *= sc;

                var s = new Soldier
                {
                    t = go.transform,
                    anim = go.GetComponent<Animator>(),
                    // A shorter soldier takes shorter steps. Tying speed to the
                    // scale as well as to its own jitter keeps the feet from
                    // sliding on the ones that are not average-sized.
                    speed = marchSpeed * sc * (1f + Random.Range(-speedJitter, speedJitter)),
                    snapPhase = placed & 3,
                };

                if (s.anim != null)
                {
                    // The rank's phase, plus a touch of its own, so ranks step
                    // together and successive ranks lag.
                    float phase = (rank * 0.16f + Random.Range(-0.04f, 0.04f)) % 1f;
                    if (phase < 0f) phase += 1f;
                    s.anim.Play("Walk", 0, phase);
                    s.anim.speed = sc;
                }

                legion.Add(s);
            }

            // Compounding gap: see DEPTH STRETCH above.
            rowZ -= rankSpacing * (1f + rank * depthStretch);
            rank++;
        }
    }

    private void SpawnMarchDust()
    {
        if (marchDustPrefab == null || bossTransform == null) return;
        var dust = Instantiate(marchDustPrefab, bossTransform.position, Quaternion.identity, bossTransform);
        dust.transform.localPosition = new Vector3(0f, 0f, -8f);
    }

    // ======================================================================
    //  Lighting
    // ======================================================================

    // ==== THE SCENE WAS LIT BY FLAT SKY AND NOTHING ELSE ====
    //
    // As authored: ambient mode Skybox at full intensity, and a WHITE
    // directional light at 0.14. So effectively all the light in the shot
    // arrived evenly from every direction at once - no key, no direction, no
    // form, and therefore no silhouette. Three hundred skeletons lit like that
    // are three hundred grey smudges.
    //
    // (The trilight colours authored in the scene - a teal sky, a purple
    // equator - were never used at all, because Skybox mode ignores them. And
    // mainLightColor / mainLightIntensity on this component were declared and
    // never read by a single line of code.)
    //
    // A night storm wants the opposite: almost no fill, and one cold weak key
    // from BEHIND the army pointing at the lens. That puts an edge on every
    // shoulder and skull in the formation and lets the mass read as a
    // silhouette - which is both the most dramatic way to shoot an army and,
    // conveniently, the one that asks least of the models. It is also what
    // makes the fog visible: a rim light is what a volumetric pass scatters.
    private void ApplyLightingRig()
    {
        if (!applyLightingRig) return;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;
        RenderSettings.ambientIntensity = 1f;

        // The volumetric fog follows this. Unity's own fog RENDER stays off -
        // the raymarched pass is doing the work, and switching the built-in one
        // on as well lays a flat distance haze over it and flattens exactly the
        // depth the volumetric pass exists to create.
        RenderSettings.fog = false;
        RenderSettings.fogColor = fogTint;

        if (mainDirectionalLight == null) return;

        mainDirectionalLight.type = LightType.Directional;
        mainDirectionalLight.color = mainLightColor;
        mainDirectionalLight.intensity = mainLightIntensity;
        mainDirectionalLight.shadows = LightShadows.Soft;
        AimMoon();

        rigColor = mainLightColor;
        rigIntensity = mainLightIntensity;

        // Several systems - the volumetric fog among them - ask RenderSettings
        // for the sun rather than hunting for a light, and it was unassigned.
        RenderSettings.sun = mainDirectionalLight;
    }

    // Behind the army, low, pointing down the line of march at the camera.
    private void AimMoon()
    {
        if (mainDirectionalLight == null || bossTransform == null) return;
        BossAxes(out Vector3 fwd, out _);
        Vector3 dir = Quaternion.Euler(0f, moonAzimuthOffset, 0f) * fwd;
        // Tilt it down by the elevation: a light travelling level with the
        // ground rims nothing, it just blinds the lens.
        Vector3 axis = Vector3.Cross(Vector3.up, dir).normalized;
        dir = Quaternion.AngleAxis(moonElevation, axis) * dir;
        mainDirectionalLight.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }

    private IEnumerator LightningStrike(int index)
    {
        if (mainDirectionalLight == null) yield break;

        Sfx(AudioID.Trailer_RiserToStrike);

        // Real lightning is not one flash. It is two or three inside a tenth of
        // a second, which is the difference between a light being switched on
        // and something happening in the sky.
        int flickers = Random.Range(2, 4);
        for (int i = 0; i < flickers; i++)
        {
            mainDirectionalLight.color = lightningColor;
            mainDirectionalLight.intensity = lightningIntensity * Random.Range(0.6f, 1f);
            yield return new WaitForSeconds(Random.Range(0.03f, 0.07f));

            mainDirectionalLight.color = rigColor;
            mainDirectionalLight.intensity = rigIntensity;
            yield return new WaitForSeconds(Random.Range(0.02f, 0.06f));
        }

        mainDirectionalLight.color = rigColor;
        mainDirectionalLight.intensity = rigIntensity;

        // Each strike is closer than the last, so the gap to its thunder
        // shortens. Nothing tells an audience a storm is bearing down on them
        // more cheaply than that.
        float delay = Mathf.Max(0.12f, thunderDelay * Mathf.Pow(0.62f, index));
        yield return new WaitForSeconds(delay);
        Sfx(AudioID.Trailer_ThunderClose);
    }

    // ======================================================================
    //  Audio
    // ======================================================================

    private void Sfx(string id)
    {
        if (!playAudio || AudioManager.Instance == null) return;
        if (!AudioManager.Instance.HasEvent(id)) return;
        AudioManager.Instance.PlaySFX(id);
    }

    private void Sfx3D(string id, Vector3 at)
    {
        if (!playAudio || AudioManager.Instance == null) return;
        if (!AudioManager.Instance.HasEvent(id)) return;
        AudioManager.Instance.PlaySFX3D(id, at);
    }

    // ==== THE EPISODE HAD NO SOUND AT ALL ====
    //
    // Not one AudioSource in the scene - while the project already declares and
    // registers a complete trailer vocabulary that nothing here was using:
    // Trailer_Music, Trailer_WindDesolate, Trailer_MarchLoop, Trailer_WarHorn,
    // Trailer_BossStep, Trailer_BoneRattle, Trailer_ThunderClose,
    // Trailer_RiserToStrike, Trailer_Whoosh, Trailer_Impact.
    //
    // Every call is guarded by HasEvent, so a bank without these authored stays
    // silent rather than breaking - but the hooks are where they belong now,
    // and filling one in is a matter of assigning a clip.
    private void StartAudioBed()
    {
        if (!playAudio || AudioManager.Instance == null || mainCamera == null) return;

        if (AudioManager.Instance.HasEvent(AudioID.Trailer_Music))
            AudioManager.Instance.PlayMusic(AudioID.Trailer_Music);

        if (AudioManager.Instance.HasEvent(AudioID.Trailer_WindDesolate))
            windHandle = AudioManager.Instance.PlayLoopingSFX3D(AudioID.Trailer_WindDesolate, mainCamera);

        if (AudioManager.Instance.HasEvent(AudioID.Trailer_MarchLoop) && bossTransform != null)
            marchHandle = AudioManager.Instance.PlayLoopingSFX3D(AudioID.Trailer_MarchLoop, bossTransform);
    }

    private void StopAudioBed()
    {
        if (AudioManager.Instance == null) return;
        if (windHandle != -1) { AudioManager.Instance.StopLoopingSFX(windHandle, 0.5f); windHandle = -1; }
        if (marchHandle != -1) { AudioManager.Instance.StopLoopingSFX(marchHandle, 0.5f); marchHandle = -1; }
    }

    // ======================================================================
    //  Per-frame
    // ======================================================================

    private void Update()
    {
        float dt = Time.deltaTime;
        episodeElapsed += dt;

        if (isBossMarching && bossTransform != null)
        {
            bossTransform.Translate(Vector3.forward * marchSpeed * dt);
            SnapToGround(bossTransform);
        }

        if (isArmyMarching)
        {
            // Grounding three hundred transforms means three hundred terrain
            // samples a frame for a field that barely undulates. Each soldier
            // re-grounds on every fourth frame instead, staggered by index, and
            // nothing about it is visible.
            int phase = Time.frameCount & 3;
            for (int i = 0; i < legion.Count; i++)
            {
                var s = legion[i];
                if (s.t == null) continue;
                s.t.Translate(Vector3.forward * s.speed * dt);
                if (s.snapPhase == phase) SnapToGround(s.t);
            }
        }

        // The boss's footfalls. The impulse DECAYS: setting it for one frame
        // and easing the camera toward it, which is what this used to do, moved
        // the camera by about two per cent of the intended amount and vanished,
        // so the giant's steps did not register at all.
        if (isBossMarching)
        {
            stepTimer += dt * marchSpeed;
            if (stepTimer >= stepFrequency)
            {
                stepTimer = 0f;
                stepImpulse = stepShakeIntensity;
                if (bossTransform != null)
                {
                    Sfx3D(AudioID.Trailer_BossStep, bossTransform.position);
                    if (stepDustPrefab != null)
                        Destroy(Instantiate(stepDustPrefab, bossTransform.position, Quaternion.identity), 4f);
                }
            }
        }
        stepImpulse = Mathf.MoveTowards(stepImpulse, 0f, dt * Mathf.Max(0.01f, stepShakeIntensity) * 5f);

        // Lightning runs on the EPISODE clock, not on a shot, so a strike can
        // land across a cut - which is exactly when it is most effective.
        while (lightningTimes != null && nextLightning < lightningTimes.Length
               && episodeElapsed >= lightningTimes[nextLightning])
        {
            StartCoroutine(LightningStrike(nextLightning));
            nextLightning++;
        }

        if (cameraDriven)
        {
            shotElapsed += dt;
            DriveCamera(dt);
        }
    }

    private void SnapToGround(Transform obj)
    {
        Vector3 pos = obj.position;
        pos.y = GetTerrainHeight(pos);
        obj.position = pos;
    }

    private float GetTerrainHeight(Vector3 position)
    {
        if (Terrain.activeTerrain != null)
            return Terrain.activeTerrain.SampleHeight(position) + Terrain.activeTerrain.transform.position.y;
        return position.y;
    }

    // ======================================================================
    //  The episode
    // ======================================================================

    private IEnumerator CinematicRoutine()
    {
        // Walk the shot list. The cuts ARE the structure, so they live here
        // rather than being implied by a pile of WaitForSeconds elsewhere.
        for (int i = 0; i < shots.Length; i++)
        {
            if (i > 0) CutTo(i);
            else { shotIndex = 0; shotElapsed = 0f; }

            float d = Mathf.Max(0.1f, shots[i].duration);
            while (shotElapsed < d) yield return null;
        }

        // The last shot has run out, which is the cue. Freeze the camera where
        // it ended - from here the throw owns it.
        cameraDriven = false;
        isBossMarching = false;
        if (bossAnimator != null) bossAnimator.SetTrigger("Throw");

        StartCoroutine(StopArmyWithInertia());

        // Slow motion is a ramp, not a switch. Snapping timeScale from 1 to
        // 0.15 and back reads as the game stuttering rather than as the moment
        // stretching - there is no acceleration, so the eye registers a dropped
        // frame instead of a held breath.
        StartCoroutine(RampTimeScale(0.15f, 0.12f));
        StartCoroutine(SmoothZoomRoutine(cinematicZoomFOV, animationWindupTime));
        yield return new WaitForSecondsRealtime(animationWindupTime);

        // Release flinch - the camera recoils a touch as the arm comes through.
        if (mainCamera != null)
        {
            mainCamera.position -= mainCamera.forward * 0.2f;
            mainCamera.position += new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f), 0f);
        }

        yield return StartCoroutine(RampTimeScale(1f, 0.08f));

        Sfx(AudioID.Trailer_Whoosh);
        if (bossWeapon != null) bossWeapon.parent = null;
        StartCoroutine(WeaponFlightRoutine());
    }

    private IEnumerator RampTimeScale(float target, float duration)
    {
        float start = Time.timeScale;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, duration);
            Time.timeScale = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        Time.timeScale = target;
    }

    // ==== THREE HUNDRED SKELETONS DO NOT STOP ON ONE FRAME ====
    //
    // Setting anim.speed to zero froze the entire legion mid-stride, all of
    // them on the same frame, which looks like the game hitching rather than
    // like an army halting. Easing the playback to nothing over a third of a
    // second lets each finish the step it was in - and because they were
    // started at rank-staggered phases, they settle raggedly, front rank
    // first, which is how a real formation stops.
    private IEnumerator StopArmyWithInertia()
    {
        yield return new WaitForSecondsRealtime(0.3f);
        isArmyMarching = false;
        Sfx(AudioID.Trailer_BoneRattle);

        float t = 0f;
        const float settle = 0.32f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / settle;
            float s = Mathf.SmoothStep(1f, 0f, t);
            for (int i = 0; i < legion.Count; i++)
                if (legion[i].anim != null) legion[i].anim.speed = s;
            yield return null;
        }
        for (int i = 0; i < legion.Count; i++)
            if (legion[i].anim != null) legion[i].anim.speed = 0f;

        if (marchHandle != -1 && AudioManager.Instance != null)
        {
            AudioManager.Instance.StopLoopingSFX(marchHandle, 0.45f);
            marchHandle = -1;
        }
    }

    private IEnumerator SmoothZoomRoutine(float targetFOV, float duration)
    {
        if (cam == null) yield break;
        float startFOV = cam.fieldOfView;
        float t = 0;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, duration);
            cam.fieldOfView = Mathf.LerpUnclamped(startFOV, targetFOV, zoomCurve.Evaluate(t));
            yield return null;
        }
    }

    private IEnumerator WeaponFlightRoutine()
    {
        if (bossWeapon == null || mainCamera == null) yield break;

        // A thrown weapon is scenery in flight - nothing on it should collide
        // with the ground or the army on the way past.
        foreach (var c in bossWeapon.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        var trails = bossWeapon.GetComponentsInChildren<TrailRenderer>(true);
        foreach (var tr in trails) { tr.Clear(); tr.enabled = true; tr.emitting = true; }

        Vector3 startPos = bossWeapon.position;
        Vector3 originalScale = bossWeapon.localScale;
        Quaternion startRot = bossWeapon.rotation;

        // ==== IT HAS TO PASS THE LENS, NOT PARK IN FRONT OF IT ====
        //
        // The target used to be twenty centimetres IN FRONT of the camera, so
        // the axe decelerated into a stop just short of the glass and sat there
        // while the impact ran. Aiming BEHIND the lens means the last frames
        // are the weapon filling and leaving the frame, and the eye never sees
        // it stop.
        Vector3 aim = mainCamera.position - mainCamera.forward * throwOvershoot;

        // One fixed axis across the flight, worked out up front. Rotate() about
        // the weapon's own right accumulated a different tumble every run and
        // was frame-rate dependent into the bargain.
        Vector3 flightDir = (aim - startPos).normalized;
        Vector3 spinAxis = Vector3.Cross(flightDir, Vector3.up);
        if (spinAxis.sqrMagnitude < 0.001f) spinAxis = Vector3.right;
        spinAxis.Normalize();

        float t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, weaponFlightDuration);
            float k = weaponFlightCurve.Evaluate(t);

            // Re-read the camera each frame: a throw aimed at where the lens
            // WAS misses it by however far it has drifted since.
            Vector3 target = mainCamera.position - mainCamera.forward * throwOvershoot;
            Vector3 pos = Vector3.LerpUnclamped(startPos, target, k);
            pos.y += Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * weaponArcHeight;
            bossWeapon.position = pos;

            // Scale on a curve rather than linearly, so it reads as
            // perspective: almost nothing for most of the flight, then
            // everything in the last few frames.
            bossWeapon.localScale = originalScale * Mathf.Lerp(1f, impactScaleMultiplier, k * k);
            bossWeapon.rotation = Quaternion.AngleAxis(throwSpins * 360f * k, spinAxis) * startRot;

            yield return null;
        }

        foreach (var tr in trails) tr.emitting = false;
        StartCoroutine(CameraImpactRoutine());
    }

    private IEnumerator CameraImpactRoutine()
    {
        // The hit: a hard stop, a blow-out from the key light, and a
        // ROTATIONAL kick. A translation alone reads as the camera being
        // nudged; a camera that is struck rolls.
        Time.timeScale = 0f;
        Sfx(AudioID.Trailer_Impact);

        if (mainCamera != null)
        {
            Quaternion preHit = mainCamera.rotation;
            mainCamera.position -= mainCamera.forward * 0.6f;
            mainCamera.rotation = preHit * Quaternion.Euler(Random.Range(-9f, -4f),
                                                            Random.Range(-6f, 6f),
                                                            Random.Range(12f, 22f));
        }

        if (mainDirectionalLight != null)
        {
            mainDirectionalLight.color = lightningColor;
            mainDirectionalLight.intensity = 20f;
        }

        yield return new WaitForSecondsRealtime(0.08f);

        // ==== SMASH TO BLACK ====
        //
        // The old ending panned up to an empty sky over six tenths of a second.
        // A slow, perfectly controlled move immediately after a weapon hits the
        // lens tells the audience nothing actually happened, and it ends the
        // episode on a shot of nothing, which is a hard place to cut from.
        //
        // The axe filling the frame IS the wipe. Take the screen on contact and
        // hold it; the next episode starts from black, which is a cut rather
        // than a transition that has to be watched.
        CanvasGroup veil = smashToBlack ? BuildImpactVeil() : null;

        if (veil != null)
        {
            float f = 0f;
            while (f < 1f)
            {
                f += Time.unscaledDeltaTime / Mathf.Max(0.01f, impactFadeDuration);
                veil.alpha = Mathf.Clamp01(f);
                yield return null;
            }
            veil.alpha = 1f;
        }

        // Only once the screen is covered does anything get put back, so the
        // audience never sees the light snap or the camera reset.
        Time.timeScale = 1f;
        if (mainDirectionalLight != null)
        {
            mainDirectionalLight.color = rigColor;
            mainDirectionalLight.intensity = rigIntensity;
        }
        StopAudioBed();

        yield return new WaitForSecondsRealtime(holdBlackDuration);
        onEpisodeFinished?.Invoke();
    }

    // Built in code so the episode carries its own transition and cannot be
    // broken by somebody rearranging the scene's canvases.
    private CanvasGroup BuildImpactVeil()
    {
        var go = new GameObject("[TrailerImpactVeil]");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        var imgGo = new GameObject("Veil", typeof(RectTransform));
        imgGo.transform.SetParent(go.transform, false);
        var img = imgGo.AddComponent<UnityEngine.UI.Image>();
        img.color = impactFadeColor;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        return group;
    }
}
