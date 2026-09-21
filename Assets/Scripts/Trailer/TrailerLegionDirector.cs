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

    [Header("Rise intro (plays before the march)")]
    // ==== THEY COME OUT OF THE GROUND, THEN THEY WALK ====
    //
    // A separate beat in front of the march, in the same scene: the field is
    // empty, the crust breaks, an army claws its way out, and a blackout hands
    // over to the march already on its feet.
    //
    // The risers are DELIBERATELY FEW. Three hundred of them rising and three
    // hundred more spawning for the march is six hundred instantiations and
    // twice the VFX for no visible gain - a low, close shot of breaking ground
    // only ever shows the forty in front of the lens. What sells "an army" here
    // is the boss coming up last and nearest, not a headcount nobody can see.
    [Tooltip("Play the rising beat before the march. Off, the episode starts at the march exactly as before.")]
    public bool playRiseIntro = true;
    [Tooltip("Off, the built-in low shot of the breaking ground is used and the field below is ignored. Tick it once you have framed your own.")]
    public bool useAuthoredRiseShot = false;
    [Tooltip("Your own framing for the rise. Only read when the box above is ticked - Unity never leaves a serialized class field null, so 'empty means built-in' cannot be detected any other way.")]
    public Shot riseShot;
    [Tooltip("How many break the crust. This is a close shot - forty is a wall of them.")]
    public int riseCount = 44;
    [Tooltip("Radius around the boss they come up in.")]
    public float riseSpread = 9f;
    [Tooltip("How far under the ground each one starts.")]
    public float riseDepth = 2.3f;
    [Tooltip("Seconds for one skeleton to come up.")]
    public float riseDuration = 2.1f;
    [Tooltip("Seconds across which the whole field breaks. They must not all arrive together, or it reads as one object being raised on a lift.")]
    public float riseWindow = 2.6f;
    [Tooltip("The resurrect state on the shared enemy animator. It is an orphan state with no transitions, which is exactly why playing it by name is safe - nothing will transition away from it.")]
    public string riseState = "Skeletons_Death_Resurrect";
    [Tooltip("Seconds they stand still, risen, before the blackout.")]
    public float riseHold = 1.1f;
    [Tooltip("What they settle into once they are up. The resurrect state is an orphan with no exit transition, so without this they HOLD ITS LAST FRAME - forty statues, perfectly still, for the whole hold.")]
    public string idleState = "Idle_A";
    [Tooltip("Dirt thrown up as each one breaks through. Falls back to the footfall puff.")]
    public GameObject riseDustPrefab;

    [Header("Blackout between the rise and the march")]
    [Tooltip("Fire a lightning strike on the blackout, so the screen is taken by a flash rather than by a fade. Far more cinematic, and it hides the hand-over completely.")]
    public bool blackoutOnLightning = true;
    public float blackoutIn = 0.09f;
    public float blackoutHold = 0.45f;
    public float blackoutOut = 0.4f;

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
    // ==== 0.72 METRES ON A CAMERA 0.85 METRES UP ====
    //
    // This was 0.3 against a shot response of 2.4, which is nearly three
    // quarters of a metre of vertical travel on a lens that is standing less
    // than a metre off the grass. The camera fell almost to the ground and
    // sprang back, every three quarters of a second. That is not a shake, it is
    // a trapdoor.
    //
    // It is metres of drop now, at response 1, and a boot landing moves a
    // camera by centimetres. There is a hard ceiling underneath it too, so no
    // combination of the two numbers can produce a lurch again.
    [Tooltip("Metres the camera drops on the boss's footfall, at a shot response of 1. A real impact moves a camera a few centimetres.")]
    public float stepShakeIntensity = 0.05f;
    [Tooltip("Hard ceiling on the drop, whatever the intensity and the shot's response multiply out to.")]
    public float maxShakeDrop = 0.13f;
    [Tooltip("Degrees of pitch per metre of drop. A jolted camera TILTS - pure vertical translation reads as an elevator, and it is the part that makes people queasy.")]
    public float stepShakePitch = 7f;
    [Tooltip("Spring stiffness of the recovery. Higher settles faster.")]
    public float shakeStiffness = 320f;
    [Tooltip("Damping ratio. Below 1 gives a little overshoot on the way back, which is what makes it read as weight rather than as a lerp.")]
    [Range(0.2f, 1.4f)] public float shakeDamping = 0.7f;
    [Tooltip("Seconds between the boss's steps, scaled by march speed.")]
    public float stepFrequency = 1.5f;
    [Tooltip("Optional puff spawned under the boss on each footfall.")]
    public GameObject stepDustPrefab;
    [Tooltip("Size of the footfall puff. These prefabs are authored for a spell going off, not for a boot, so they arrive several times too big.")]
    [Range(0.05f, 2f)] public float stepDustScale = 0.3f;
    [Tooltip("Fraction of the prefab's authored particle count. A puff under one foot does not need a spell's worth of smoke.")]
    [Range(0.05f, 2f)] public float stepDustDensity = 0.3f;
    [Tooltip("Seconds before a footfall puff is cleaned up. It was four, and with a step every three quarters of a second that is five of them on screen at once.")]
    public float stepDustLifetime = 2.2f;

    [Tooltip("Optional dust the marching army drags with it. Parented to the boss and left running.")]
    public GameObject marchDustPrefab;
    [Range(0.05f, 4f)] public float marchDustScale = 1.4f;
    [Range(0.05f, 2f)] public float marchDustDensity = 0.5f;

    [Header("Weather")]
    [Tooltip("Rain, parented to the camera so it travels with the shot. Assets/VFX Brady Games/Particle Effect/Heavy Rain.prefab is in the project and its material is already on a URP particle shader.")]
    public GameObject rainPrefab;
    [Tooltip("How far above the lens the rain volume sits. It has to be high enough that drops are already falling when they enter frame.")]
    public float rainHeight = 9f;
    [Tooltip("Child emitters whose name contains this are switched off. The rain prefab ships with a 'Splash' system that bursts a droplet wherever a drop lands - authored for a shot looking at a puddle, and at a legion's distance it is just speckle over the whole frame.")]
    public string rainSplashFilter = "Splash";

    [Header("Animator (EnemyAnimator.controller)")]
    // ==== IT WAS PLAYING A STATE AND A TRIGGER THAT DO NOT EXIST ====
    //
    // The shared EnemyAnimator has exactly these parameters - isMoving, Die,
    // Attack, Hit, Stagger, IsRanged, Aim - and these states: Idle_A,
    // Running_A, Attack, Bow_Attack, Hit_A, Death_A, Dizzy.
    //
    // There is no "Walk" state, so Animator.Play("Walk") on three hundred
    // minions did nothing at all and the entire legion slid forward standing in
    // Idle_A. And there is no "Throw" trigger, so the boss's throw never fired
    // either - which is the other half of why he was walking in his idle: not
    // only was nothing telling him to move, the one thing he was told to do was
    // addressed to a parameter the controller has never had.
    //
    // Names are fields because a rig swap should be an inspector edit, not a
    // hunt through a crowd script for a hard-coded string.
    [Tooltip("The bool that moves the shared enemy animator out of its idle.")]
    public string movingBool = "isMoving";
    [Tooltip("The locomotion STATE, played directly so each rank can be given its own phase in the cycle.")]
    public string walkState = "Running_A";
    [Tooltip("Preferred throw trigger. Falls back below when the controller has no such parameter.")]
    public string throwTrigger = "Throw";
    [Tooltip("What the boss does instead when there is no throw animation. EnemyAnimator only has Attack.")]
    public string throwFallbackTrigger = "Attack";

    [Header("Audio")]
    [Tooltip("Play the trailer score and the march bed. Every event is guarded, so a project with these unassigned is silent rather than broken.")]
    public bool playAudio = true;

    [Header("The throw")]
    // ==== A THROW ANIMATION WITHOUT TOUCHING THE SHARED CONTROLLER ====
    //
    // EnemyAnimator.controller has no throw - its states are Idle_A, Running_A,
    // Attack, Bow_Attack, Hit_A, Death_A, Dizzy - and it is the controller
    // EVERY enemy in the game runs on. Adding a state and a trigger to it to
    // serve one shot in one trailer puts the whole game's combat animation on
    // the line for a cutscene.
    //
    // It does not need to be touched. The skeleton rig is Humanoid and the
    // Attack state plays a humanoid clip, so a humanoid throw retargets onto it
    // for free - and an AnimatorOverrideController swaps that one clip for this
    // one boss, at runtime, in this scene only. Nothing on disk changes and no
    // other enemy is affected.
    [Tooltip("The throw. Any HUMANOID clip retargets onto the skeleton - Assets/MainCharacters/Animations/Throw.anim is in the project and is 1.37s. Leave empty to just take a swing with Attack.")]
    public AnimationClip throwClip;
    [Tooltip("Name of the clip currently on the Attack state, which is the one the throw replaces. Matched case-insensitively, with a fallback to the first non-bow clip containing 'Attack'.")]
    public string attackClipName = "Melee_1H_Attack_Stab";
    [Tooltip("The Attack STATE's name, watched so the weapon leaves on the right frame.")]
    public string attackStateName = "Attack";
    [Tooltip("How far through the throw the axe leaves his hand. 0.45 is about where an overarm release sits - tune it against the clip, not against a stopwatch.")]
    [Range(0f, 1f)] public float releaseAtNormalizedTime = 0.45f;
    [Tooltip("Play the throw at full speed while the WORLD is in slow motion. The arm comes through crisply against a slowed field, which is the whole point of the slow-mo; left off, the windup is stretched to several seconds of real time and reads as a freeze.")]
    public bool throwOnUnscaledTime = true;

    [Tooltip("No longer the release timing - the animation decides that now. This is the ceiling on how long to wait for it before releasing anyway.")]
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

    [Tooltip("Optional closing beat: the map of Aethelgard, and the curse taking all of it. Runs after the axe hits the lens. Leave empty and the episode ends on the smash to black exactly as before.")]
    public TrailerMapCurse mapCurse;

    // ---- runtime ------------------------------------------------------------

    private class Soldier
    {
        public Transform t;
        public Animator anim;
        public float speed;
        public int snapPhase;      // which frame in the stagger this one grounds on
    }

    private readonly List<Soldier> legion = new List<Soldier>(320);

    private class Riser
    {
        public Transform t;
        public Animator anim;
        public float startAt;
        public float fromY, toY;
        public bool started;
    }
    private readonly List<Riser> risers = new List<Riser>(64);
    private CanvasGroup veil;
    private Shot overrideShot;

    private bool isBossMarching = true;
    private bool isArmyMarching = true;
    private bool cameraDriven = true;

    private int shotIndex;
    private float shotElapsed;
    private float episodeElapsed;
    private float stepTimer;
    private float shakeOffset;     // current drop, metres (negative = down)
    private float shakeVel;
    private float groundY;         // smoothed ground under the lens
    private bool groundYValid;
    private float driftSeed;
    private int nextLightning;

    private Camera cam;
    private float rigIntensity;
    private Color rigColor;
    private int windHandle = -1, marchHandle = -1, rainHandle = -1;

    // ==== THE CAST IS ACTING IN A FILM, NOT PLAYING THE GAME ====
    //
    // The boss prefab carries TutorialBossAI and the trailer minion still
    // carries EnemyAI, and both were left running.
    //
    // TutorialBossAI looks for a player, finds none, and writes isMoving=false
    // to the animator every frame - which is precisely why the boss walked the
    // whole episode playing his IDLE. Worse, its Start calls ActivateBoss,
    // which puts the boss HEALTH BAR on screen over the trailer. And EnemyAI on
    // three hundred minions is three hundred searches for a player that does
    // not exist, three hundred CharacterControllers shoving each other apart,
    // and a spawn-rise coroutine that can leave them frozen mid-emerge.
    //
    // None of them are fighting anybody. They are extras: the director moves
    // them and the director animates them.
    private static void StripGameplay(GameObject go)
    {
        if (go == null) return;

        // ==== DISABLING AN AI IS NOT THE SAME AS REMOVING IT ====
        //
        // This used to set `enabled = false` on EnemyAI and TutorialBossAI, and
        // that is why the aggro barks and the minimap dots survived it. Awake
        // and OnEnable have ALREADY RUN by the time an instantiated object can
        // be touched - the minimap renderer is created in there - and a
        // coroutine started in one of them keeps running after its component is
        // disabled. Disabling only stops Update.
        //
        // TrailerPuppet has solved this properly for a while: DestroyImmediate,
        // so the component is gone before its Start can register anything, plus
        // the two entries every reimplementation forgets - the runtime
        // MinimapOnly renderer and the world-space health canvas. Borrowing it
        // rather than writing a fourth version of the same list.
        TrailerPuppet.Strip(go);

        // Not in that list, because puppets there have no controller: three
        // hundred bodies standing in each other's laps shove each other out of
        // formation if anything is solid.
        foreach (var c in go.GetComponentsInChildren<CharacterController>(true)) if (c != null) c.enabled = false;
    }

    // Awake, not Start: a component that is disabled before the first frame
    // never gets its own Start, which is how ActivateBoss is headed off.
    private void Awake()
    {
        if (bossTransform != null) StripGameplay(bossTransform.gameObject);
    }

    // ==== A REFERENCE THAT POINTS AT THE PREFAB IS NOT A REFERENCE TO THE BOSS ====
    //
    // bossAnimator was serialized as {fileID, guid, type: 3} - the form that
    // addresses a component INSIDE THE PREFAB ASSET ON DISK, not the instance
    // standing in the scene. (bossTransform right next to it was a plain scene
    // id, which is what the correct form looks like.)
    //
    // Nothing errors. SetBool and Play happily write to the asset's animator,
    // the skeleton on screen never hears a word of it, and the boss walks his
    // whole march in Idle_A and then declines to throw - which is exactly what
    // it did, twice, through two rounds of looking for the fault in the
    // controller instead.
    //
    // A component that belongs to no loaded scene cannot be the one being
    // filmed, and that is cheap to notice. Noticed once, at startup, loudly.
    private void ResolveBossAnimator()
    {
        if (bossTransform == null) return;

        bool wrong = bossAnimator == null
                  || !bossAnimator.gameObject.scene.IsValid()
                  || !bossAnimator.transform.IsChildOf(bossTransform);

        if (!wrong) return;

        Animator found = bossTransform.GetComponentInChildren<Animator>(true);
        Debug.LogWarning(bossAnimator == null
            ? "[TrailerLegion] bossAnimator was not assigned."
            : "[TrailerLegion] bossAnimator does not belong to the boss in this scene - it is almost certainly a " +
              "reference to the PREFAB ASSET rather than to the instance. Every animator write was going nowhere.", this);

        if (found != null)
        {
            bossAnimator = found;
            Debug.LogWarning($"[TrailerLegion] Using '{found.name}' on the boss instead. Re-drag the field in the " +
                             "inspector to make this permanent.", this);
        }
        else
        {
            Debug.LogError("[TrailerLegion] ...and the boss has no Animator anywhere in its hierarchy, so he will " +
                           "neither walk nor throw.", this);
        }
    }

    private void Start()
    {
        cam = mainCamera != null ? mainCamera.GetComponent<Camera>() : null;
        ResolveBossAnimator();
        if (shots == null || shots.Length == 0) shots = BuildDefaultShots();
        if (!useAuthoredRiseShot || riseShot == null) riseShot = BuildDefaultRiseShot();

        HideMinimapLayers();
        ApplyLightingRig();

        // The legion is NOT spawned yet when there is a rise beat - it arrives
        // under the blackout, so nothing of the hand-over is ever on screen.
        if (playRiseIntro) SpawnRisers();
        else SpawnLegion();

        SpawnMarchDust();
        SpawnRain();
        StartAudioBed();

        InstallThrowClip();

        if (playRiseIntro)
        {
            // He is in the ground too, and he comes up last.
            isBossMarching = false;
            isArmyMarching = false;
        }
        else if (bossAnimator != null)
        {
            bossAnimator.SetBoolSafe(movingBool, true);
            if (!string.IsNullOrEmpty(walkState)) bossAnimator.Play(walkState, 0, Random.value);
        }

        driftSeed = Random.Range(0f, 100f);
        // The rise drives the camera from its own coroutine, so Update must not
        // also advance it - two drivers double the shot clock.
        cameraDriven = !playRiseIntro;
        if (playRiseIntro) overrideShot = riseShot;

        // Frame the first shot before anything renders, so the episode does not
        // open on one frame of wherever the camera happened to be parked.
        DriveCamera(0f, true);

        StartCoroutine(CinematicRoutine());
    }

    // ==== THE MINIMAP DRAWS INTO ANY CAMERA THAT WILL HAVE IT ====
    //
    // Enemy markers are not a component that can be removed per prefab - they
    // are a RENDER LAYER. EnemyAI builds a dedicated renderer on MinimapOnly at
    // runtime, and the boss prefab carries a MiniMapIcon child on that layer as
    // well. Stripping them off each body works, and it only works for bodies
    // this director happens to create.
    //
    // The camera is the single place that settles it: a cinematic camera has no
    // business rendering the minimap's layers at all, so it stops being asked
    // to. Anything added to the scene later is covered for free.
    private void HideMinimapLayers()
    {
        if (cam == null) return;
        int only = LayerMask.NameToLayer("MinimapOnly");
        int gfx = LayerMask.NameToLayer("MinimapGraphics");
        int mask = cam.cullingMask;
        if (only >= 0) mask &= ~(1 << only);
        if (gfx >= 0) mask &= ~(1 << gfx);
        cam.cullingMask = mask;
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
            startOffset = new Vector3(1.2f, 0.85f, 15f),
            endOffset = new Vector3(0.9f, 0.95f, 13.2f),
            lookOffset = new Vector3(0f, 0.7f, 0f),
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

    // Down at the crust, close, on a longish lens so the ground fills the frame
    // and there is no horizon to tell you how many of them there are. The
    // camera creeps IN as they come up, so the last thing to arrive - the boss
    // - arrives into a tighter frame than the first.
    private Shot BuildDefaultRiseShot()
    {
        return new Shot
        {
            label = "0 - Rising",
            duration = 5.8f,
            startOffset = new Vector3(2.8f, 1.25f, 8.5f),
            endOffset = new Vector3(1.7f, 0.95f, 6.4f),
            lookOffset = new Vector3(0f, 0.45f, -1.2f),
            startFOV = 41f,
            endFOV = 36f,
            roll = -2f,
            handheld = 0.08f,
            // The boss breaking through kicks the lens by hand (CameraKick);
            // there are no footfalls in this beat to respond to.
            shakeResponse = 1f,
            ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
        };
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

    [Tooltip("The camera is never allowed closer than this to the ground. A lens a few centimetres up clips through the surface on the first bump, which reads as the camera falling under the terrain.")]
    public float minGroundClearance = 0.7f;

    // A camera position: grounded, then lifted by the offset's Y. Grounding it
    // is what keeps a low shot low over a rolling field instead of burying the
    // lens on the next rise.
    private Vector3 CameraPoint(Vector3 o)
    {
        BossAxes(out Vector3 fwd, out Vector3 right);
        Vector3 p = bossTransform.position + fwd * o.z + right * o.x;

        // ==== THE GROUND IS ALSO A SOURCE OF BOB ====
        //
        // The lens is re-grounded at its OWN xz every frame while travelling at
        // two metres a second, so on anything but a billiard table it rides
        // every bump in the field. Horizontal tracking wants to be tight;
        // vertical wants to be lazy. Smoothing the ground height on its own,
        // far slower than the position, keeps the framing locked while the
        // terrain stops being felt.
        float ground = GetTerrainHeight(p);
        if (!groundYValid) { groundY = ground; groundYValid = true; }
        else groundY = Mathf.Lerp(groundY, ground, 1f - Mathf.Exp(-3.5f * Mathf.Max(0.0001f, Time.deltaTime)));

        // Clearance floor: see minGroundClearance. A shot authored at thirty
        // centimetres will still clip the ground the moment the field is not
        // perfectly flat, and a camera that clips the ground looks exactly like
        // a camera that has fallen through it.
        p.y = groundY + Mathf.Max(o.y, minGroundClearance);
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

        Shot s = overrideShot ?? shots[shotIndex];
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

        // The footfall drops the camera and it recovers. See the spring above.
        float drop = Mathf.Clamp(shakeOffset * s.shakeResponse, -maxShakeDrop, maxShakeDrop);
        Vector3 want = basePos + drift + new Vector3(0f, drop, 0f);

        // ==== AND NEVER THROUGH THE FLOOR ====
        //
        // The clearance floor was applied to the BASE position and the shake was
        // added afterwards, so the jolt could put the lens under the grass on
        // its way down - which is most of what made it read as a jump rather
        // than as a knock.
        float clearance = GetTerrainHeight(want) + minGroundClearance;
        if (want.y < clearance) want.y = clearance;
        // A cut is instant; within a shot the camera is smoothed a little so
        // the terrain grounding does not read as a stair-step.
        mainCamera.position = snap ? want : Vector3.Lerp(mainCamera.position, want, 1f - Mathf.Exp(-12f * dt));

        Quaternion aim = Quaternion.LookRotation((look - mainCamera.position).normalized, Vector3.up);
        // The tilt that comes with the knock. Small, and it does most of the
        // work: a camera that only moves vertically reads as an elevator.
        float pitch = -drop * stepShakePitch;
        if (Mathf.Abs(s.roll) > 0.001f || Mathf.Abs(pitch) > 0.001f)
            aim *= Quaternion.Euler(pitch, 0f, s.roll);
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
        groundYValid = false;   // the new shot starts on its own ground, not the last one's

        shakeOffset = 0f;
        shakeVel = 0f;
        DriveCamera(0f, true);
        ClearRain();

        if (shotIndex == 1) OnReveal();
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
                StripGameplay(go);

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
                    s.anim.SetBoolSafe(movingBool, true);
                    if (!string.IsNullOrEmpty(walkState)) s.anim.Play(walkState, 0, phase);
                    s.anim.speed = sc;
                }

                legion.Add(s);
            }

            // Compounding gap: see DEPTH STRETCH above.
            rowZ -= rankSpacing * (1f + rank * depthStretch);
            rank++;
        }
    }

    private ParticleSystem[] rainSystems;

    private void SpawnRain()
    {
        if (rainPrefab == null || mainCamera == null) return;
        var rain = Instantiate(rainPrefab, mainCamera.position + Vector3.up * rainHeight, Quaternion.identity, mainCamera);
        rain.transform.localPosition = new Vector3(0f, rainHeight, 0f);
        // Level, whatever the camera is doing. A rain volume that inherits a
        // dutch angle rains sideways.
        rain.transform.rotation = Quaternion.identity;

        // ==== THE SPLASH IS AUTHORED FOR A DIFFERENT SHOT ====
        //
        // The prefab is two emitters: the fall, and a 'Splash' that bursts a
        // droplet wherever a drop lands. That second one is written for a
        // camera looking down at wet ground a couple of metres away. Out here,
        // with the volume strapped to a camera that is fifteen metres up on the
        // wide, it is a field of speckle across the whole frame with nothing
        // for the eye to attach it to.
        //
        // Killed by name rather than by index, so re-ordering the prefab or
        // swapping in a different rain does not silently start removing the
        // wrong emitter.
        if (!string.IsNullOrEmpty(rainSplashFilter))
        {
            foreach (var ps in rain.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null || ps.gameObject == rain) continue;
                if (ps.name.IndexOf(rainSplashFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.gameObject.SetActive(false);
            }
        }

        rainSystems = rain.GetComponentsInChildren<ParticleSystem>(true);
    }

    // A world-space rain volume attached to a camera that CUTS leaves every
    // live drop smeared across the new shot. Clearing it on the cut costs one
    // frame of rain and saves the cut.
    private void ClearRain()
    {
        if (rainSystems == null) return;
        for (int i = 0; i < rainSystems.Length; i++)
            if (rainSystems[i] != null) rainSystems[i].Clear(true);
    }

    // A disc of skeletons buried around the boss, plus the boss himself. They
    // are placed at their final xz and simply start below the ground; nothing
    // moves horizontally, so nothing can drift out of frame on the way up.
    private void SpawnRisers()
    {
        risers.Clear();
        if (skeletonPrefab == null || bossTransform == null) return;

        BossAxes(out Vector3 fwd, out Vector3 right);

        for (int i = 0; i < riseCount; i++)
        {
            // Square-rooted radius so they spread EVENLY over the disc. A plain
            // random radius bunches everything at the centre, which is exactly
            // where the camera is looking.
            float ang = Random.value * Mathf.PI * 2f;
            float rad = Mathf.Sqrt(Random.value) * riseSpread;
            Vector3 pos = bossTransform.position + right * (Mathf.Cos(ang) * rad) + fwd * (Mathf.Sin(ang) * rad);
            float ground = GetTerrainHeight(pos);

            Quaternion rot = bossTransform.rotation * Quaternion.Euler(0f, Random.Range(-yawJitter * 3f, yawJitter * 3f), 0f);
            GameObject go = Instantiate(skeletonPrefab, new Vector3(pos.x, ground - riseDepth, pos.z), rot);
            StripGameplay(go);
            go.transform.localScale *= Random.Range(scaleJitter.x, scaleJitter.y);

            var r = new Riser
            {
                t = go.transform,
                anim = go.GetComponent<Animator>(),
                // The near ones come up LAST, so the wave travels toward the
                // lens rather than away from it.
                startAt = Random.Range(0f, riseWindow),
                fromY = ground - riseDepth,
                toY = ground,
            };
            if (r.anim != null) r.anim.speed = 0f;   // held until its moment
            risers.Add(r);
        }

        // And the boss, deepest and last.
        Vector3 bp = bossTransform.position;
        bossGroundY = GetTerrainHeight(bp);
        bossTransform.position = new Vector3(bp.x, bossGroundY - riseDepth * 1.25f, bp.z);
        if (bossAnimator != null) { bossAnimator.speed = 0f; }
    }

    private float bossGroundY;

    private IEnumerator RiseSequence()
    {
        overrideShot = riseShot;
        shotElapsed = 0f;
        groundYValid = false;
        DriveCamera(0f, true);

        // The camera move is stretched to cover the beat rather than the beat
        // being cut to fit the camera: tune riseWindow / riseDuration / riseHold
        // and the framing follows, instead of drifting out of step with them.
        float total = riseWindow + riseDuration + riseHold;
        overrideShot.duration = Mathf.Max(0.5f, total);

        float bossStart = riseWindow * 0.82f;   // he is the last thing up
        bool bossStarted = false;
        float lastRiseSfx = -1f;
        float elapsed = 0f;

        while (elapsed < riseWindow + riseDuration)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            shotElapsed += dt;

            for (int i = 0; i < risers.Count; i++)
            {
                var r = risers[i];
                if (r.t == null) continue;

                if (!r.started)
                {
                    if (elapsed < r.startAt) continue;
                    r.started = true;
                    if (r.anim != null)
                    {
                        r.anim.speed = 1f;
                        if (!string.IsNullOrEmpty(riseState)) r.anim.Play(riseState, 0, 0f);
                    }
                    SpawnRiseDust(new Vector3(r.t.position.x, r.toY, r.t.position.z));

                    // Rate-limited: forty of these inside three seconds is a
                    // wall of identical one-shots, not an event.
                    if (elapsed - lastRiseSfx > 0.14f)
                    {
                        lastRiseSfx = elapsed;
                        Cue3D(AudioID.Trailer_BoneRise, AudioID.Enemy_Spawn, r.t.position);
                    }
                }

                float k = Mathf.Clamp01((elapsed - r.startAt) / Mathf.Max(0.05f, riseDuration));
                // Slow to break the crust, then out in a rush - the opposite of
                // a lift, which is what a linear lerp looks like.
                float eased = k * k * (3f - 2f * k);
                eased = Mathf.Pow(eased, 0.7f);
                Vector3 p = r.t.position;
                p.y = Mathf.Lerp(r.fromY, r.toY, eased);
                r.t.position = p;
            }

            if (!bossStarted && elapsed >= bossStart)
            {
                bossStarted = true;
                if (bossAnimator != null)
                {
                    bossAnimator.speed = 1f;
                    if (!string.IsNullOrEmpty(riseState)) bossAnimator.Play(riseState, 0, 0f);
                }
                SpawnRiseDust(new Vector3(bossTransform.position.x, bossGroundY, bossTransform.position.z));
                Cue3D(AudioID.Trailer_BoneRise, AudioID.Enemy_Spawn, bossTransform.position);
                CameraKick();
            }
            if (bossStarted && bossTransform != null)
            {
                float bk = Mathf.Clamp01((elapsed - bossStart) / Mathf.Max(0.05f, riseDuration * 1.35f));
                float be = Mathf.Pow(bk * bk * (3f - 2f * bk), 0.7f);
                Vector3 bp = bossTransform.position;
                bp.y = Mathf.Lerp(bossGroundY - riseDepth * 1.25f, bossGroundY, be);
                bossTransform.position = bp;
            }

            DriveCamera(dt);
            yield return null;
        }

        // ==== AND THEN THEY BREATHE ====
        //
        // The resurrect state has no exit transition - that is why playing it by
        // name is safe - but it also means the clip ENDS and holds its final
        // frame. Forty skeletons frozen mid-pose for the whole hold is the one
        // thing that would give away that they are props. Crossfaded into idle,
        // they settle instead.
        for (int i = 0; i < risers.Count; i++)
        {
            var a = risers[i].anim;
            if (a != null && !string.IsNullOrEmpty(idleState)) a.CrossFade(idleState, 0.3f);
        }
        if (bossAnimator != null && !string.IsNullOrEmpty(idleState)) bossAnimator.CrossFade(idleState, 0.35f);

        // Standing. Let it sit - a beat of an army that has just stopped moving
        // is what makes the blackout land.
        float hold = 0f;
        while (hold < riseHold)
        {
            hold += Time.deltaTime;
            shotElapsed += Time.deltaTime;
            DriveCamera(Time.deltaTime);
            yield return null;
        }
    }

    private void SpawnRiseDust(Vector3 at)
    {
        GameObject prefab = riseDustPrefab != null ? riseDustPrefab : stepDustPrefab;
        if (prefab == null) return;
        var puff = Instantiate(prefab, at, Quaternion.identity);
        ScaleEffect(puff, stepDustScale * 1.35f, stepDustDensity);
        Destroy(puff, stepDustLifetime);
    }

    // A jolt on the spring the footfalls already use, so the boss breaking
    // through shakes the lens the same way his steps will.
    private void CameraKick()
    {
        shakeVel -= stepShakeIntensity * 2.2f * Mathf.Sqrt(Mathf.Max(1f, shakeStiffness));
    }

    // ==== THE HAND-OVER HAPPENS BEHIND A FLASH ====
    //
    // A cut from a field of risen skeletons to a marching column is a cut
    // between two versions of the same subject, and those are the ones an
    // audience notices. So it is not a cut: lightning takes the screen, the
    // blackout lands on the flash, and everything - destroying the risers,
    // building the formation, putting the boss back on his feet - happens while
    // there is nothing to see. The march then opens FROM black, which reads as
    // a new shot rather than as a change of contents.
    private IEnumerator BlackoutHandover()
    {
        // A high index on purpose: the thunder delay shortens with it, so the
        // strike that takes the screen cracks almost on the flash rather than
        // rumbling in a second later, over black, from nowhere.
        if (blackoutOnLightning) StartCoroutine(LightningStrike(3));

        // The score has been held down since the first frame. It comes up HALF
        // way here and the rest at the reveal - one lift across nine seconds
        // reads as the track simply starting late; two reads as a build.
        if (playAudio && AudioManager.Instance != null) AudioManager.Instance.DuckMusicInstance(0.6f, 0.8f);

        EnsureVeil();

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, blackoutIn);
            if (veil != null) veil.alpha = Mathf.Clamp01(t);
            yield return null;
        }
        if (veil != null) veil.alpha = 1f;

        // --- nothing below this line is visible ---
        for (int i = 0; i < risers.Count; i++)
            if (risers[i].t != null) Destroy(risers[i].t.gameObject);
        risers.Clear();

        if (bossTransform != null)
        {
            Vector3 bp = bossTransform.position;
            bossTransform.position = new Vector3(bp.x, bossGroundY, bp.z);
        }
        if (bossAnimator != null)
        {
            bossAnimator.speed = 1f;
            bossAnimator.SetBoolSafe(movingBool, true);
            if (!string.IsNullOrEmpty(walkState)) bossAnimator.Play(walkState, 0, Random.value);
        }

        SpawnLegion();
        isBossMarching = true;
        isArmyMarching = true;
        overrideShot = null;
        stepTimer = 0f;
        shakeOffset = 0f;
        shakeVel = 0f;
        // --- visible again from here ---

        yield return new WaitForSecondsRealtime(blackoutHold);
    }

    private IEnumerator LiftVeil()
    {
        if (veil == null) yield break;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, blackoutOut);
            veil.alpha = 1f - Mathf.Clamp01(t);
            yield return null;
        }
        veil.alpha = 0f;
    }

    private void SpawnMarchDust()
    {
        if (marchDustPrefab == null || bossTransform == null) return;
        var dust = Instantiate(marchDustPrefab, bossTransform.position, Quaternion.identity, bossTransform);
        dust.transform.localPosition = new Vector3(0f, 0f, -8f);
        ScaleEffect(dust, marchDustScale, marchDustDensity);
    }

    // ==== A VFX PACK'S PUFF IS SIZED FOR A SPELL ====
    //
    // These prefabs are authored to read as magic going off, so dropped under a
    // boot at their authored size they are a smokescreen: too large, too many,
    // and lasting long enough that five of them overlap between footfalls.
    //
    // Transform scale alone does not do it. A ParticleSystem defaults to Local
    // scaling mode, which ignores its PARENTS' scale entirely - so scaling the
    // root of a prefab whose emitters are children changes nothing, which is
    // the usual reason "I scaled it down and it did not get smaller". Every
    // system is switched to Hierarchy first, and the particle count is scaled
    // separately, because a smaller puff made of the same number of particles
    // is just a denser puff.
    private static void ScaleEffect(GameObject go, float scale, float density)
    {
        if (go == null) return;
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);

        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null) continue;

            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var em = ps.emission;
            em.rateOverTimeMultiplier *= density;
            em.rateOverDistanceMultiplier *= density;

            // Bursts carry their count separately from the rate, and a puff of
            // this kind is usually ALL burst - leaving them alone is why
            // turning the rate down often does nothing at all.
            int n = em.burstCount;
            if (n > 0)
            {
                var bursts = new ParticleSystem.Burst[n];
                em.GetBursts(bursts);
                for (int i = 0; i < n; i++)
                {
                    var b = bursts[i];
                    b.count = new ParticleSystem.MinMaxCurve(
                        Mathf.Max(1f, b.count.constantMin * density),
                        Mathf.Max(1f, b.count.constantMax * density));
                    bursts[i] = b;
                }
                em.SetBursts(bursts);
            }
        }
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

        Cue(AudioID.Trailer_RiserToStrike, AudioID.Enemy_Telegraph);

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
        Cue(AudioID.Trailer_ThunderClose, AudioID.Env_Thunder);
    }

    // ======================================================================
    //  Audio
    // ======================================================================

    // ==== EVERY TRAILER EVENT IN THE PROJECT IS EMPTY ====
    //
    // All seventeen Trailer_* SoundGroups are declared, registered in the sound
    // dictionary, and have no FMOD event assigned - so the whole vocabulary the
    // episode was written against is silent, and will stay silent until someone
    // authors those events in the FMOD project. Meanwhile there are seventy-
    // seven events that DO exist, several of which are close enough to carry
    // the beat today.
    //
    // So every cue is a pair: the event it WANTS, and something real behind it.
    // The episode has a full soundtrack now, and the moment a proper trailer
    // event is authored it takes over on its own with no code change. A cue
    // whose fallback is also missing is silent rather than an error.
    private string Pick(string primary, string fallback)
    {
        var am = AudioManager.Instance;
        if (am == null) return null;
        if (!string.IsNullOrEmpty(primary) && am.HasEvent(primary)) return primary;
        if (!string.IsNullOrEmpty(fallback) && am.HasEvent(fallback)) return fallback;
        return null;
    }

    private void Cue(string primary, string fallback = null)
    {
        if (!playAudio) return;
        string id = Pick(primary, fallback);
        if (id != null) AudioManager.Instance.PlaySFX(id);
    }

    private void Cue3D(string primary, string fallback, Vector3 at)
    {
        if (!playAudio) return;
        string id = Pick(primary, fallback);
        if (id != null) AudioManager.Instance.PlaySFX3D(id, at);
    }

    private int Loop(string primary, string fallback, Transform follow)
    {
        if (!playAudio || follow == null) return -1;
        string id = Pick(primary, fallback);
        return id != null ? AudioManager.Instance.PlayLoopingSFX3D(id, follow) : -1;
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

        string music = Pick(AudioID.Trailer_Music, AudioID.Music_Battle);
        if (music != null)
        {
            AudioManager.Instance.PlayMusic(music);
            // The score opens held DOWN. Shot one is meant to be a field, some
            // rain and something walking towards you; a full trailer cue over
            // it announces that this is a trailer and removes the only thing
            // the shot has, which is not knowing yet. It comes up on the cut.
            AudioManager.Instance.DuckMusicInstance(0.22f, 0.01f);
        }

        windHandle = Loop(AudioID.Trailer_WindDesolate, AudioID.Ambient_Wind, mainCamera);
        rainHandle = Loop(AudioID.Ambient_Rain, AudioID.Env_Thunder, mainCamera);

        // A single crow over the empty field, before anything else happens.
        // It is the cheapest way to say "this place was already dead".
        StartCoroutine(DelayedCue(0.55f, AudioID.Trailer_Crows, AudioID.Ambient_Crow));
    }

    private IEnumerator DelayedCue(float delay, string primary, string fallback)
    {
        yield return new WaitForSeconds(delay);
        Cue(primary, fallback);
    }

    // The reveal cut. Everything that was being held back arrives at once.
    private void OnReveal()
    {
        Cue(AudioID.Trailer_WarHorn, AudioID.Boss_Roar);
        // No horde growl. Its fallback was the enemy AGGRO cue - a gameplay
        // sound that says "something has noticed you", which is the opposite of
        // what a legion that has been walking since before the shot started
        // should sound like. It stays out until a real Trailer_HordeGrowl
        // exists to put here.
        Cue(AudioID.Trailer_HordeGrowl, null);
        if (playAudio && AudioManager.Instance != null) AudioManager.Instance.UnduckMusicInstance(1.2f);
        marchAudible = true;
    }

    // ==== A MARCH IS NOT A LOOP ====
    //
    // Trailer_MarchLoop does not exist, and a single looping stomp under three
    // hundred men would be the wrong answer even if it did: it plays from one
    // point, it repeats on a fixed period, and the ear finds the seam in about
    // four seconds.
    //
    // Footfalls are scattered through the formation instead - a handful a
    // second, each from a DIFFERENT soldier's actual position. It is
    // spatialised, it never repeats, it gets denser as the camera closes on
    // them because the near ranks are louder, and it costs nothing to author.
    private bool marchAudible;
    private float marchStepTimer;

    private void UpdateMarchFootfalls(float dt)
    {
        if (!marchAudible || !isArmyMarching || legion.Count == 0) return;
        if (!playAudio || AudioManager.Instance == null) return;

        marchStepTimer -= dt;
        if (marchStepTimer > 0f) return;
        marchStepTimer = Random.Range(0.11f, 0.2f);

        var s = legion[Random.Range(0, legion.Count)];
        if (s.t == null) return;
        Cue3D(AudioID.Trailer_MarchLoop, AudioID.Enemy_Footstep, s.t.position);

        // And, rarely, bone on bone from somewhere in the mass.
        if (Random.value < 0.12f)
        {
            var b = legion[Random.Range(0, legion.Count)];
            // Also no fallback here. Enemy_Spawn is a rise-from-the-ground
            // growl, and firing it out of the formation several times a second
            // turned the march into a pack of animals.
            if (b.t != null) Cue3D(AudioID.Trailer_BoneRattle, null, b.t.position);
        }
    }

    // Clears the field for the climax: the march stops being audible, the beds
    // fade, and the music goes to nothing. Deliberately NOT StopAudioBed - the
    // handles are released here on purpose so the impact has nothing under it.
    private void DropOut()
    {
        marchAudible = false;
        if (!playAudio || AudioManager.Instance == null) return;

        AudioManager.Instance.DuckMusicInstance(0f, 0.18f);
        if (windHandle != -1) { AudioManager.Instance.StopLoopingSFX(windHandle, 0.3f); windHandle = -1; }
        if (rainHandle != -1) { AudioManager.Instance.StopLoopingSFX(rainHandle, 0.3f); rainHandle = -1; }
    }

    private void StopAudioBed()
    {
        if (AudioManager.Instance == null) return;
        marchAudible = false;
        if (windHandle != -1) { AudioManager.Instance.StopLoopingSFX(windHandle, 0.5f); windHandle = -1; }
        if (rainHandle != -1) { AudioManager.Instance.StopLoopingSFX(rainHandle, 0.5f); rainHandle = -1; }
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
                // A kick to the spring's VELOCITY, not a jump to a position.
                // Snapping the offset to full amplitude and decaying it back was
                // a teleport on the frame it fired, with a straight-line return
                // behind it - no attack, no settle, nothing that reads as mass.
                shakeVel -= stepShakeIntensity * Mathf.Sqrt(Mathf.Max(1f, shakeStiffness));
                if (bossTransform != null)
                {
                    Cue3D(AudioID.Trailer_BossStep, AudioID.Enemy_Footstep, bossTransform.position);
                    if (stepDustPrefab != null)
                    {
                        var puff = Instantiate(stepDustPrefab, bossTransform.position, Quaternion.identity);
                        ScaleEffect(puff, stepDustScale, stepDustDensity);
                        Destroy(puff, stepDustLifetime);
                    }
                }
            }
        }
        // Damped spring back to rest. Integrated in fixed sub-steps so a frame
        // spike cannot make a stiff spring explode.
        {
            float k = Mathf.Max(1f, shakeStiffness);
            float c = 2f * Mathf.Sqrt(k) * shakeDamping;
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 8);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                shakeVel += (-k * shakeOffset - c * shakeVel) * h;
                shakeOffset += shakeVel * h;
            }
            shakeOffset = Mathf.Clamp(shakeOffset, -maxShakeDrop, maxShakeDrop);
        }

        // Lightning runs on the EPISODE clock, not on a shot, so a strike can
        // land across a cut - which is exactly when it is most effective.
        while (lightningTimes != null && nextLightning < lightningTimes.Length
               && episodeElapsed >= lightningTimes[nextLightning])
        {
            StartCoroutine(LightningStrike(nextLightning));
            nextLightning++;
        }

        UpdateMarchFootfalls(dt);

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

    // ==== A SAMPLE TAKEN OFF THE EDGE OF THE TERRAIN RETURNS ZERO ====
    //
    // Terrain.SampleHeight does not clamp to the terrain's bounds, and the wide
    // shot places the camera fifty-four metres in front of the boss - far
    // enough to leave the map near an edge. Off the terrain it returns zero, so
    // with this terrain sitting at the origin and its surface a hundred and
    // fifty metres up, the camera was placed a hundred and fifty metres BELOW
    // the ground, on the exact frame of a cut. That is the camera jumping under
    // the terrain.
    //
    // The boss is always standing on the ground, so his own height is a
    // reference for what a plausible answer looks like. A sample that disagrees
    // with him by more than a cliff's worth is not a hill, it is a miss.
    private float GetTerrainHeight(Vector3 position)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null) return bossTransform != null ? bossTransform.position.y : position.y;

        float h = terrain.SampleHeight(position) + terrain.transform.position.y;

        if (bossTransform != null && Mathf.Abs(h - bossTransform.position.y) > implausibleDrop)
        {
            if (!_warnedOffTerrain)
            {
                _warnedOffTerrain = true;
                Debug.LogWarning($"[TrailerLegion] A ground sample at {position} came back {h:F1} while the boss is " +
                                 $"standing at {bossTransform.position.y:F1}. That point is almost certainly off the " +
                                 "terrain - pull the shot offsets in, or make the terrain bigger. Using the boss's " +
                                 "height instead so the camera does not drop through the world.", this);
            }
            return bossTransform.position.y;
        }
        return h;
    }

    [Tooltip("A ground sample this far from the boss's own feet is treated as a miss rather than as terrain. Raise it only if the episode genuinely marches across a cliff.")]
    public float implausibleDrop = 60f;

    private bool _warnedOffTerrain;

    // ======================================================================
    //  The episode
    // ======================================================================

    private IEnumerator CinematicRoutine()
    {
        if (playRiseIntro)
        {
            yield return StartCoroutine(RiseSequence());
            yield return StartCoroutine(BlackoutHandover());
            cameraDriven = true;
            // The march opens FROM black: the veil lifts over the first seconds
            // of shot one rather than before it, so the cut and the first move
            // are the same gesture.
            StartCoroutine(LiftVeil());
        }

        // Walk the shot list. The cuts ARE the structure, so they live here
        // rather than being implied by a pile of WaitForSeconds elsewhere.
        for (int i = 0; i < shots.Length; i++)
        {
            // ==== INCLUDING THE FIRST ONE ====
            //
            // Shot zero used to only set the index, on the reasoning that Start
            // had already framed it. That stopped being true the moment a rise
            // beat went in front of the march: the camera is now parked down in
            // the grass at the end of a completely different shot, and without a
            // snap it LERPS across to the march's opening frame - visibly,
            // because the veil is lifting over exactly those frames. It also
            // carried the rise's smoothed ground and drift phase across with it.
            CutTo(i);

            float d = Mathf.Max(0.1f, shots[i].duration);
            while (shotElapsed < d) yield return null;
        }

        // The last shot has run out, which is the cue. Freeze the camera where
        // it ended - from here the throw owns it.
        cameraDriven = false;
        isBossMarching = false;
        if (bossAnimator != null)
        {
            bossAnimator.SetBoolSafe(movingBool, false);
            TriggerThrow(bossAnimator);
        }

        // ==== THE LOUDEST THING AVAILABLE IS SILENCE ====
        //
        // Everything has been building - score, rain, wind, three hundred pairs
        // of feet - and the temptation at the climax is to add to it. The
        // opposite is stronger. The bed drops out as his arm goes back, a riser
        // fills the gap, and then the riser stops too: the axe crosses the last
        // ten metres in near-total silence, and the impact lands in a vacuum.
        //
        // It costs nothing, it needs no new audio authored, and it is the
        // difference between an ending that is loud and one that hits.
        DropOut();
        Cue(AudioID.Trailer_RiserToStrike, AudioID.Enemy_Telegraph);

        StartCoroutine(StopArmyWithInertia());

        // Slow motion is a ramp, not a switch. Snapping timeScale from 1 to
        // 0.15 and back reads as the game stuttering rather than as the moment
        // stretching - there is no acceleration, so the eye registers a dropped
        // frame instead of a held breath.
        StartCoroutine(RampTimeScale(0.15f, 0.12f));
        StartCoroutine(SmoothZoomRoutine(cinematicZoomFOV, animationWindupTime));
        yield return StartCoroutine(WaitForRelease());

        // Release flinch - the camera recoils a touch as the arm comes through.
        if (mainCamera != null)
        {
            mainCamera.position -= mainCamera.forward * 0.2f;
            mainCamera.position += new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f), 0f);
        }

        yield return StartCoroutine(RampTimeScale(1f, 0.08f));

        Cue(AudioID.Trailer_Whoosh, AudioID.Cinematic_Whoosh);
        if (bossWeapon != null) bossWeapon.parent = null;
        StartCoroutine(WeaponFlightRoutine());
    }

    private void InstallThrowClip()
    {
        if (throwClip == null || bossAnimator == null) return;

        RuntimeAnimatorController rac = bossAnimator.runtimeAnimatorController;
        if (rac == null)
        {
            Debug.LogWarning("[TrailerLegion] The boss has no animator controller, so the throw clip cannot be " +
                             "installed.", this);
            return;
        }

        // Wrap whatever is there. If it is already an override controller, keep
        // its existing overrides and add to them.
        var ov = rac as AnimatorOverrideController ?? new AnimatorOverrideController(rac);

        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        ov.GetOverrides(pairs);

        AnimationClip target = null;
        // Exact name first.
        for (int i = 0; i < pairs.Count && target == null; i++)
        {
            var orig = pairs[i].Key;
            if (orig != null && string.Equals(orig.name, attackClipName, System.StringComparison.OrdinalIgnoreCase))
                target = orig;
        }
        // Then the first melee attack clip, so a renamed asset does not silently
        // leave the boss swinging instead of throwing.
        for (int i = 0; i < pairs.Count && target == null; i++)
        {
            var orig = pairs[i].Key;
            if (orig == null) continue;
            if (orig.name.IndexOf("Attack", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (orig.name.IndexOf("Bow", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            target = orig;
        }

        if (target == null)
        {
            Debug.LogWarning($"[TrailerLegion] No clip named '{attackClipName}' on the boss's controller and no " +
                             "other melee attack clip to replace, so he will swing rather than throw.", this);
            return;
        }

        ov[target] = throwClip;
        bossAnimator.runtimeAnimatorController = ov;
        Debug.Log($"[TrailerLegion] Throw installed: '{target.name}' -> '{throwClip.name}' on this boss only.");
    }

    // Holds until the throw animation reaches the frame the hand opens, so the
    // axe leaves WITH the arm instead of after a fixed stopwatch that had no
    // relationship to the animation at all - and none at all in slow motion,
    // where 0.8 seconds of real time was about a tenth of a second of arm.
    private IEnumerator WaitForRelease()
    {
        if (bossAnimator == null)
        {
            yield return new WaitForSecondsRealtime(animationWindupTime);
            yield break;
        }

        AnimatorUpdateMode previous = bossAnimator.updateMode;
        if (throwOnUnscaledTime) bossAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;

        // Generous ceiling: a missing state or a clip that never arrives must
        // not strand the episode a frame before its climax.
        float guard = 0f;
        float limit = Mathf.Max(0.3f, animationWindupTime) * 4f;
        bool entered = false;

        while (guard < limit)
        {
            guard += Time.unscaledDeltaTime;
            var st = bossAnimator.GetCurrentAnimatorStateInfo(0);
            if (st.IsName(attackStateName))
            {
                entered = true;
                if (st.normalizedTime >= releaseAtNormalizedTime) break;
            }
            else if (entered)
            {
                // It has already been through and moved on; do not wait out the
                // rest of the ceiling.
                break;
            }
            yield return null;
        }

        if (!entered)
            Debug.LogWarning($"[TrailerLegion] The animator never entered '{attackStateName}', so the release is on " +
                             "the timeout rather than on the animation. Check the state name.", this);

        if (throwOnUnscaledTime) bossAnimator.updateMode = previous;
    }

    // Prefer a real throw animation; take a swing if the controller has none.
    // SetTriggerSafe alone would have silently done nothing, which is exactly
    // what it was doing.
    private void TriggerThrow(Animator a)
    {
        if (a == null) return;
        if (HasParam(a, throwTrigger)) { a.SetTrigger(throwTrigger); return; }
        if (HasParam(a, throwFallbackTrigger)) { a.SetTrigger(throwFallbackTrigger); return; }
        Debug.LogWarning($"[TrailerLegion] The boss animator has neither '{throwTrigger}' nor " +
                         $"'{throwFallbackTrigger}', so he releases the axe without an animation.", this);
    }

    private static bool HasParam(Animator a, string name)
    {
        if (a == null || string.IsNullOrEmpty(name) || a.runtimeAnimatorController == null) return false;
        var ps = a.parameters;
        for (int i = 0; i < ps.Length; i++) if (ps[i].name == name) return true;
        return false;
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
        Cue(AudioID.Trailer_BoneRattle, null);

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
Cue(AudioID.Trailer_Impact, AudioID.Region_Shockwave);
        Cue(AudioID.Env_StoneBreak, null);

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
        if (smashToBlack)
        {
            EnsureVeil();
            float f = 0f;
            while (f < 1f && veil != null)
            {
                f += Time.unscaledDeltaTime / Mathf.Max(0.01f, impactFadeDuration);
                veil.alpha = Mathf.Clamp01(f);
                yield return null;
            }
            if (veil != null) veil.alpha = 1f;
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

        // ==== AND THEN THE MAP ====
        //
        // Everything below happens behind the same black the smash left up: the
        // map is built, the lens is placed square on it, and the roll it still
        // carries from being hit is set. Only then does the veil lift, so the
        // shot OPENS on a map that is already standing there. The alternative is
        // a frame or two of one arriving, which is the one thing that would read
        // as a menu being opened rather than as a reveal.
        if (mapCurse != null && mapCurse.Prepare(mainCamera))
        {
            yield return StartCoroutine(LiftVeil());
            yield return StartCoroutine(mapCurse.Run(mainCamera));

            // Back to black on the way out, so whatever follows this episode
            // starts from the same place the march did.
            EnsureVeil();
            float f = 0f;
            while (f < 1f && veil != null)
            {
                f += Time.unscaledDeltaTime / Mathf.Max(0.01f, blackoutOut);
                veil.alpha = Mathf.Clamp01(f);
                yield return null;
            }
            if (veil != null) veil.alpha = 1f;
            mapCurse.Cleanup();
            yield return new WaitForSecondsRealtime(holdBlackDuration);
        }

        onEpisodeFinished?.Invoke();
    }

    // Built in code so the episode carries its own transition and cannot be
    // broken by somebody rearranging the scene's canvases.
    // One veil for the whole episode: the blackout between the rise and the
    // march, and the smash at the end, are the same piece of black.
    private void EnsureVeil()
    {
        if (veil == null) veil = BuildImpactVeil();
    }

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
