using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Trailer, the beat the march has been building to — two armies already running
// at each other, arrows crossing above them, and the frame taken away on
// contact.
//
// ==== WHY THIS AND NOT THE STAGED FIGHT ====
//
// TrailerBattleDirector blocks a duel: a ring, a counter, a knockdown, a rise.
// Every one of those has to be choreographed, timed and shot, and any of them
// landing badly reads as a bad fight rather than as a bad shot.
//
// This is the opposite trade. One idea, one camera position, no choreography:
// the player's army on one side with the hero in front of his own line, the
// legion from the march on the other, the lens low and side-on in the gap
// between them. It says what the fight was going to say in a shape that cannot
// be performed badly, because nobody performs anything.
//
// The cut is the point. You do not show the collision; you take the screen away
// at the instant of it. What the audience imagines is worse than anything that
// could be animated, and it is the only ending that does not require the fight
// to be good.
//
// ==== WHY THE GAP IS THE STATE, NOT THE POSITIONS ====
//
// The obvious build gives every soldier a velocity and lets them run. A hundred
// and fifty units each integrating their own position drift apart — rounding,
// frame timing and ground snapping accumulate differently per unit — so the two
// front ranks arrive at slightly different times and the instant of contact is
// a smear that moves every time the shot is run.
//
// So there is ONE number, the gap between the two front ranks, and every unit in
// both armies is placed from it each frame. They meet exactly at the centre and
// exactly when the gap reaches impactGap, take after take.
//
// ==== EVERYTHING EXPENSIVE HAPPENS DURING THE MARCH ====
//
// A hundred and fifty characters and a hundred arrows appearing on the frame the
// march cuts would stall the editor hard — not because of the instantiation but
// because of the FIRST DRAW, where URP compiles a shader variant per pass and
// per keyword set, synchronously, on the render thread. The statue shot learned
// this the expensive way.
//
// So the armies are built at Start and stand through the whole march, kinematic
// and scaled to nothing at the meeting point: submitted for drawing every frame,
// occupying a fraction of a pixel. By the time the march ends, every variant
// this shot needs is already compiled.
[DisallowMultipleComponent]
public class TrailerClashDirector : MonoBehaviour
{
    [Header("When")]
    [Tooltip("Plays as soon as this episode reports finished — the rise and the march are one episode, and this is the beat after them. Left empty it plays on its own the moment the scene starts.")]
    public TrailerLegionDirector playAfter;

    [Header("Where")]
    [Tooltip("The point the two lines meet. Left empty, this object's own position is used.")]
    public Transform clashCentre;
    // ==== WHICH ARMY ENDS UP ON WHICH SIDE OF FRAME ====
    //
    // The camera stands at centre + side, looking back at the centre, so its
    // forward is -side and its right is up x forward — which works out to
    // exactly +advanceAxis. The legion's front rank sits at centre + axis, so
    // the LEGION IS ON THE RIGHT and the player's army on the left, whatever
    // compass direction the axis happens to point.
    //
    // That is the intended reading and it falls out of the geometry rather than
    // being arranged, so it cannot quietly flip when the axis is re-aimed.
    // Mirror Camera is there for when the shot wants the other over-the-shoulder.
    [Tooltip("The direction the LEGION faces — the player's army comes the other way. Only the horizontal part is used.")]
    public Vector3 advanceAxis = Vector3.forward;
    [Tooltip("Put the camera on the other side of the line, which swaps the two armies left-to-right in frame. Off: the player's army is on the LEFT, the legion on the RIGHT.")]
    public bool mirrorCamera;

    [Header("The player's army")]
    [Tooltip("The hero himself, out in front of his own line. Left empty, the line closes up and nobody leads it.")]
    public GameObject playerPrefab;
    public float playerLead = 3.4f;
    [Tooltip("Barracks units. Knight / Barbarian / Rogue_Hooded are the three the game hires.")]
    public GameObject[] allyPrefabs;
    [Tooltip("Ranks are what the camera sees. Files spread across each army's own front, which from side-on runs AWAY from the lens; ranks stack behind the front, which from side-on runs ACROSS the frame. So depth of formation, not width of front, is what fills the shot.")]
    public int allyCount = 56;
    public int allyRanks = 8;

    [Header("The legion")]
    public GameObject enemyPrefab;
    [Tooltip("Deliberately more of them. This is not a fair fight, it is a last stand.")]
    public int enemyCount = 120;
    public int enemyRanks = 12;

    [Header("Formation")]
    public float fileSpacing = 1.6f;
    public float rankSpacing = 1.9f;
    [Tooltip("Metres of scatter on each unit's place in the grid. Zero is a chessboard; this is what makes it an army.")]
    public float positionJitter = 0.4f;
    [Tooltip("Metres the slowest stragglers fall behind once the line is at full pace. The formation is neat while it stands and ragged while it runs, which is what stops a charge reading as one rigid block sliding across the field.")]
    public float straggle = 2.6f;
    [Tooltip("Metres between the two FRONT ranks when the shot opens. Read it together with the lens: at 48 degrees from 24 metres out the frame holds about thirty-eight metres, so a gap much wider than this opens on two crowds nobody can see.")]
    public float startGap = 32f;

    [Header("The charge")]
    // Three beats, and they have to be three or the shot is a blur:
    //   HOLD    both lines standing, the hero out in front of his. This is the
    //           only moment the audience is given to read that there are two
    //           sides and which is which, so it is not short.
    //   RUN     the horn, and they go. Together.
    //   IMPACT  the frame is taken away.
    [Tooltip("Seconds both lines stand before anything moves. This beat IS the staging — without it the shot opens mid-action and reads as chaos.")]
    public float holdSeconds = 2f;
    [Tooltip("Metres per second, per side. The gap closes at twice this, so it is half as fast as it sounds and still covers thirty metres in four seconds.")]
    public float chargeSpeed = 3.6f;
    [Tooltip("Seconds to reach full pace. An army that goes from still to sprinting on one frame reads as a playback rate.")]
    public float accelSeconds = 0.9f;
    [Tooltip("Multiplier on the number fed to the animators, separate from how fast they actually travel. Lower it if feet skate, raise it if they moonwalk.")]
    public float gaitScale = 1f;
    [Tooltip("The gap at which the frame goes black. Not zero — the cut lands BEFORE anyone interpenetrates, because nothing here is animated to collide.")]
    public float impactGap = 3.2f;

    [Tooltip("The gap at which the world starts slowing. The last few metres held long is the oldest trick there is and it works: it is the breath before the hit, and it buys the camera time to finish its push.")]
    public float slowMoAtGap = 8f;
    [Range(0.15f, 1f)]
    [Tooltip("How far down the closing speed is pulled by the time they touch.")]
    public float slowMoFactor = 0.35f;

    [Header("Camera")]
    public Camera shotCamera;
    [Tooltip("Metres to the side of the line the armies close along. The lens sits in the gap BETWEEN them and watches across it, so both armies enter from opposite edges of frame.")]
    // ==== THE FRAMING IS A MOVE, NOT A POSITION ====
    //
    // A single wide has to choose between "you can see there are two armies" and
    // "the men are big enough to matter", and picking either one loses the other
    // — which is why the first pass read as two small crowds a long way off.
    //
    // It does not have to choose. The shot OPENS wide, where the job is to state
    // that there are two sides and which is which, and it is pressing in by the
    // end, where the job is that they are men and they are about to hit each
    // other. Fourteen metres of dolly, a metre of drop and eight degrees of lens
    // over about six seconds: the figures roughly double in frame while the
    // camera never appears to do anything but lean in.
    [Tooltip("Metres to the side at the START. Wide enough to hold the gap and a slice of each army.")]
    public float cameraSide = 34f;
    [Tooltip("Metres the camera dollies in as they close. This is what stops the armies looking small: it ends at Camera Side minus this.")]
    public float cameraCreep = 14f;
    [Tooltip("Height at the start — above the heads, so the depth of both formations reads.")]
    public float cameraHeight = 3.2f;
    [Tooltip("Height at the moment of contact. Lower, so they loom over the lens instead of being looked down on.")]
    public float cameraEndHeight = 2f;
    [Tooltip("Lens at the start. At 52 degrees from 34 metres the frame holds about fifty-nine metres: a thirty-two metre gap plus roughly thirteen of each side.")]
    public float cameraFov = 52f;
    [Tooltip("Lens at the moment of contact. Tighter, with the dolly, rather than either one alone — a zoom on its own reads as a zoom.")]
    public float cameraEndFov = 44f;
    public float handheld = 0.03f;
    [Tooltip("Aim height above the meeting point. Near the camera's own height, so the horizon sits level and the shot reads as standing among them.")]
    public float aimHeight = 2.4f;

    [Header("Arrows")]
    [Tooltip("The game's own arrow. Fired in volleys from BEHIND each line, so they arc over their own army and cross in the middle of frame — the one image a low side-on camera in the gap exists to catch.")]
    public GameObject arrowPrefab;
    public int arrowsPerVolley = 24;
    [Tooltip("Gaps at which a volley leaves. One as they commit, one as they close.")]
    public float firstVolleyAtGap = 24f;
    public float secondVolleyAtGap = 13f;
    [Tooltip("Seconds a volley takes to leave. All at once is a firework; spread over a moment it is archers.")]
    public float volleyStagger = 0.45f;
    [Tooltip("Seconds of flight. Longer arcs higher, and the apex is what the camera sees.")]
    public float arrowFlightSeconds = 1.5f;
    public float arrowGravity = 22f;
    [Tooltip("Metres behind each front rank the volley launches from, and how wide across the line it spreads.")]
    public float arrowLaunchBehind = 12f;
    public float arrowSpread = 22f;
    public float arrowLaunchHeight = 1.7f;
    [Tooltip("The streak behind the head, matching the one EnemyProjectile draws in gameplay.")]
    public Color arrowTrailColor = new Color(1f, 0.82f, 0.45f, 0.9f);
    public float arrowTrailTime = 0.18f;
    public float arrowTrailWidth = 0.075f;

    [Header("Fog")]
    // The march is filmed INSIDE its fog on purpose — the warriors come out of
    // it. This shot is filmed ACROSS thirty metres of it, and at the march's
    // density that is a white wall. Thinned for this beat only, and eased rather
    // than switched, so the air clearing is part of the shot.
    [Tooltip("Extinction per metre while the armies close. The march runs at about 0.28, which is opaque past fifteen metres; the far line here is forty away.")]
    public float clashFogDensity = 0.1f;
    [Tooltip("Height of the fog layer during the shot. High enough that the men are IN it rather than standing above it.")]
    public float clashFogHeight = 26f;
    [Range(0f, 1f)]
    [Tooltip("The least density the noise may leave. At zero the noise carves clear holes, and a shot filmed straight through one of them looks as though the fog switched off — which is the other half of why thinning it read as losing it. Holding a floor gives an even haze that thins without vanishing.")]
    public float clashNoiseFloor = 0.4f;
    [Tooltip("Seconds the air takes to clear. Slow, so it happens under the held beat and is never seen as a setting changing.")]
    public float fogEaseSeconds = 3f;

    [Header("Weather")]
    [Tooltip("The same Heavy Rain prefab the march uses — the march director spawns it, and the march director has finished by the time this runs.")]
    public GameObject rainPrefab;
    public float rainHeight = 12f;

    [Header("Out")]
    public float holdBlack = 0.7f;

    [Header("Audio")]
    public string windBed = AudioID.Trailer_WindDesolate;
    public string hornCue = AudioID.Trailer_WarHorn;
    public string marchLoop = AudioID.Trailer_MarchLoop;
    public string rattleCue = AudioID.Trailer_BoneRattle;
    [Tooltip("Played one frame BEFORE the cut, so the sound of contact carries over the black instead of starting under it.")]
    public string impactCue = AudioID.Region_Shockwave;

    [Header("Diagnostics")]
    public bool autoPlay = true;
    public bool IsFinished { get; private set; }

    // ---- runtime -------------------------------------------------------------

    private class Unit
    {
        public TrailerPuppet puppet;
        public Vector3 offset;      // across (x) and back (z) within its own formation, plus jitter
        public float lag;           // metres this one trails by at full pace
        public Vector3 scale;       // the real one; parked units wear a fraction of it
        public int snapPhase;
    }

    private class Arrow
    {
        public Transform t;
        public Vector3 velocity;
        public float life;
    }

    private readonly List<Unit> allies = new List<Unit>();
    private readonly List<Unit> enemies = new List<Unit>();
    private readonly List<Transform> arrowPool = new List<Transform>();
    private readonly List<Vector3> arrowScale = new List<Vector3>();
    private readonly List<Arrow> arrowsInFlight = new List<Arrow>();
    private Unit player;

    private Vector3 centre, axis, side;
    private float gap;
    private float speed;
    private int frame;
    private int nextArrow;

    // Grounding every unit every frame is a raycast per soldier per frame. Spread
    // across four the error is a couple of centimetres of height on a figure
    // running in a straight line, for a quarter of the cost. The march director
    // learned the same thing with three hundred.
    private const int GroundStride = 4;

    // Small enough to be a fraction of a pixel, large enough that nothing culls
    // it — a parked unit still has to be DRAWN or its shaders stay uncompiled.
    private const float ParkedScale = 0.004f;

    private void Start()
    {
        if (autoPlay) StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        TrailerLogGuard.Arm();

        centre = clashCentre != null ? clashCentre.position : transform.position;
        axis = advanceAxis; axis.y = 0f;
        axis = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.forward;
        side = new Vector3(axis.z, 0f, -axis.x);      // perpendicular, on the horizontal
        if (mirrorCamera) side = -side;

        if (shotCamera == null) shotCamera = Camera.main;

        // ---- built now, filmed later ----
        BuildArmy(allies, allyPrefabs, allyCount, allyRanks, true);
        BuildArmy(enemies, enemyPrefab != null ? new[] { enemyPrefab } : null, enemyCount, enemyRanks, false);
        BuildPlayer();
        BuildArrowPool();

        gap = Mathf.Max(startGap, impactGap + 1f);
        speed = 0f;

        // ---- wait out the episode before this one ----
        if (playAfter != null)
        {
            while (!playAfter.IsFinished) yield return null;

            // Its Update still marches a boss and three hundred skeletons, and
            // its camera work would fight this one. The episode is over, so the
            // component goes with it — and so does everything it spawned, or
            // this shot is filmed over a field of figures standing perfectly
            // still in the middle distance.
            playAfter.ClearVeil();
            playAfter.TearDown();
            playAfter.enabled = false;
        }

        WakeEveryone();
        PlaceEverything(true);
        PlaceCamera(0f);
        BuildRain();
        StartCoroutine(EaseFog());

        var polish = TrailerCinematicPolish.GetOrCreate();
        polish.OpenTrailer();
        TrailerAudio.SilenceStaleBeds();
        Loop(windBed);
        Cue3D(rattleCue, centre + axis * (gap * 0.5f));

        // ---- 1. HOLD. Two lines, standing. ----
        //
        // The whole shot depends on this beat. It is the only moment the
        // audience is given to read that there are two sides, which is which,
        // and that one of them has a man standing out in front of it. Open on
        // the movement instead and every frame after it is noise.
        speed = 0f;
        RunGait();

        float t = 0f;
        while (t < holdSeconds)
        {
            t += Time.unscaledDeltaTime;
            PlaceCamera(0f);
            DriveArrows();
            yield return null;
        }

        // ---- 2. RUN. The horn, and they go together. ----
        Cue(hornCue);
        Loop(marchLoop);

        bool firedFirst = false, firedSecond = false;
        float accel = 0f;

        while (gap > impactGap)
        {
            float dt = Time.unscaledDeltaTime;

            accel = Mathf.MoveTowards(accel, 1f, dt / Mathf.Max(0.05f, accelSeconds));
            speed = chargeSpeed * accel * accel;    // eased in; still to sprinting in one frame is a playback rate

            if (!firedFirst && gap <= firstVolleyAtGap) { firedFirst = true; StartCoroutine(Volley()); }
            if (!firedSecond && gap <= secondVolleyAtGap) { firedSecond = true; StartCoroutine(Volley()); }

            // The last few metres are held long. Everything in this shot runs on
            // unscaled time, so Time.timeScale would do nothing here — what slows
            // is the closing itself, which is the only clock the shot has.
            holdFactor = 1f;
            if (slowMoAtGap > impactGap && gap < slowMoAtGap)
                holdFactor = Mathf.Lerp(slowMoFactor, 1f, (gap - impactGap) / (slowMoAtGap - impactGap));

            // Both sides close, so the gap shuts at twice one side's speed.
            gap = Mathf.Max(impactGap, gap - speed * holdFactor * 2f * dt);

            PlaceEverything(false);
            PlaceCamera(Progress());
            DriveArrows();
            yield return null;
        }

        // ---- 3. the cut ----
        //
        // The sound goes one frame early on purpose. A hit that starts on the
        // same frame as the black reads as the video file ending; a hit that
        // starts a frame BEFORE it carries across the cut, and the black becomes
        // part of the impact rather than the absence of one.
        Cue3D(impactCue, centre + Vector3.up * aimHeight);
        yield return null;

        polish.SetFlash(Color.black);
        DropOut();

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, holdBlack));
        RestoreFog();
        IsFinished = true;
    }

    private float Progress()
    {
        float span = Mathf.Max(1f, startGap - impactGap);
        return Mathf.Clamp01((startGap - gap) / span);
    }

    private float holdFactor = 1f;

    // The gait follows the HELD speed, not the intended one: if the closing
    // slows and the legs do not, the whole field sprints on the spot.
    private float Gait { get { return speed * holdFactor * gaitScale; } }

    // 0 while they stand, 1 at full pace.
    private float paceForLag { get { return chargeSpeed > 0.01f ? Mathf.Clamp01(speed / chargeSpeed) : 0f; } }

    private void RunGait()
    {
        float g = Gait;
        for (int i = 0; i < allies.Count; i++) if (allies[i].puppet != null) allies[i].puppet.SetGait(g);
        for (int i = 0; i < enemies.Count; i++) if (enemies[i].puppet != null) enemies[i].puppet.SetGait(g);
        if (player != null && player.puppet != null) player.puppet.SetGait(g);
    }

    // ---- the armies ----------------------------------------------------------

    private void BuildArmy(List<Unit> into, GameObject[] prefabs, int count, int ranks, bool ally)
    {
        if (prefabs == null || prefabs.Length == 0 || count <= 0) return;

        ranks = Mathf.Max(1, ranks);
        int files = Mathf.Max(1, Mathf.CeilToInt(count / (float)ranks));

        for (int i = 0; i < count; i++)
        {
            GameObject prefab = prefabs[i % prefabs.Length];
            if (prefab == null) continue;

            int rank = i / files;
            int file = i % files;

            // Centre the block on the axis, and stagger every other rank by half
            // a file so the lines do not read as a grid seen end-on.
            float across = (file - (files - 1) * 0.5f) * fileSpacing + (rank % 2 == 0 ? 0f : fileSpacing * 0.5f);
            float back = rank * rankSpacing;

            Vector3 facing = ally ? axis : -axis;
            var puppet = TrailerPuppet.Spawn(prefab, centre, Quaternion.LookRotation(facing), transform);
            if (puppet == null) continue;

            var u = new Unit
            {
                puppet = puppet,
                offset = new Vector3(across + Random.Range(-positionJitter, positionJitter),
                                     0f,
                                     back + Random.Range(-positionJitter, positionJitter)),
                // How far this one falls behind once the line is running. Applied
                // in proportion to the pace, so the formation stands NEAT during
                // the hold and strings out as they go — which is both what
                // happens and what stops a charging line reading as one rigid
                // object sliding across the field.
                lag = Random.Range(0f, straggle),
                scale = puppet.transform.localScale,
                snapPhase = i % GroundStride,
            };
            // Only the side whose materials are NOT already on screen has to be
            // drawn while it waits. The legion is the same prefab the march has
            // three hundred of, so its variants have been compiled since the
            // first frame; parking those hidden saves a hundred draw submissions
            // a frame through the whole march for nothing given up.
            Park(u, ally);
            into.Add(u);
        }
    }

    private void BuildPlayer()
    {
        if (playerPrefab == null) return;

        var puppet = TrailerPuppet.Spawn(playerPrefab, centre, Quaternion.LookRotation(axis), transform);
        if (puppet == null) return;

        GameObject go = puppet.gameObject;

        // ==== THE HERO IS NOT AN ENEMY, SO Strip DOES NOT COVER HIM ====
        //
        // TrailerPuppet.Strip knows about EnemyAI, agents and health canvases.
        // The player prefab has none of those and everything else instead:
        // PlayerController reads input and would walk him out of the shot,
        // PlayerSpawnManager would move him to a spawn point, and the
        // CharacterController makes him solid enough to shove his own front rank
        // out of line. DestroyImmediate, not disable, for the reason Strip gives:
        // a component queued for destruction still gets its Start, and Start is
        // where each of these does the thing.
        foreach (var c in go.GetComponentsInChildren<PlayerController>(true)) if (c != null) DestroyImmediate(c);
        foreach (var c in go.GetComponentsInChildren<PlayerSpawnManager>(true)) if (c != null) DestroyImmediate(c);
        foreach (var c in go.GetComponentsInChildren<CharacterController>(true)) if (c != null) c.enabled = false;

        // He is the figure the shot is composed around, and he is a modular rig
        // whose skinned bounds come off a root bone rather than the pose. Any one
        // of the crowd can afford to be wrong about its own bounds; he cannot.
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr != null) smr.updateWhenOffscreen = true;

        player = new Unit
        {
            puppet = puppet,
            offset = new Vector3(0f, 0f, -playerLead),   // ahead of his own front rank
            scale = puppet.transform.localScale,
            snapPhase = 0,
        };
        Park(player, true);
    }

    private void Park(Unit u, bool keepDrawn)
    {
        u.puppet.transform.position = centre;
        u.puppet.transform.localScale = u.scale * ParkedScale;
        u.puppet.SetGait(0f);
        if (!keepDrawn) u.puppet.gameObject.SetActive(false);
    }

    private void WakeEveryone()
    {
        WakeAll(allies);
        WakeAll(enemies);
        Wake(player);
    }

    private static void WakeAll(List<Unit> army)
    {
        for (int i = 0; i < army.Count; i++) Wake(army[i]);
    }

    private static void Wake(Unit u)
    {
        if (u == null || u.puppet == null) return;
        u.puppet.gameObject.SetActive(true);
        u.puppet.transform.localScale = u.scale;
    }

    // Every unit, every frame, from the one number.
    private void PlaceEverything(bool groundAll)
    {
        frame++;

        Vector3 allyFront = centre - axis * (gap * 0.5f);
        Vector3 enemyFront = centre + axis * (gap * 0.5f);

        Place(allies, allyFront, -axis, axis, groundAll);
        Place(enemies, enemyFront, axis, -axis, groundAll);

        if (player != null && player.puppet != null)
        {
            Vector3 p = allyFront + side * player.offset.x - axis * player.offset.z;
            bool ground = groundAll || (frame % GroundStride) == player.snapPhase;
            player.puppet.Place(p, axis, ground);
            player.puppet.SetGait(Gait);
        }
    }

    // `back` is the direction the formation extends away from its own front rank;
    // `facing` is where its soldiers look.
    private void Place(List<Unit> army, Vector3 front, Vector3 back, Vector3 facing, bool groundAll)
    {
        for (int i = 0; i < army.Count; i++)
        {
            Unit u = army[i];
            if (u.puppet == null) continue;

            Vector3 p = front + side * u.offset.x + back * (u.offset.z + u.lag * paceForLag);
            bool ground = groundAll || (frame % GroundStride) == u.snapPhase;
            u.puppet.Place(p, facing, ground);
            u.puppet.SetGait(Gait);
        }
    }

    // ---- arrows --------------------------------------------------------------

    private void BuildArrowPool()
    {
        if (arrowPrefab == null || arrowsPerVolley <= 0) return;

        // Two volleys, both sides — and built now, parked, for the same reason
        // the soldiers are.
        int total = arrowsPerVolley * 4;
        for (int i = 0; i < total; i++)
        {
            var go = Instantiate(arrowPrefab, centre, Quaternion.identity, transform);
            go.name = "Arrow_" + i;

            // The gameplay arrow carries its own flight logic, its own damage and
            // its own raycast. None of that belongs in a shot where it hits
            // nothing and everything is placed by hand; the MODEL is what is
            // wanted, so the behaviour comes off and this drives the geometry.
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) DestroyImmediate(mb);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) if (c != null) c.enabled = false;
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) if (rb != null) rb.isKinematic = true;

            AddTrail(go);

            arrowScale.Add(go.transform.localScale);
            go.transform.localScale *= ParkedScale;
            arrowPool.Add(go.transform);
        }
    }

    // The same streak EnemyProjectile draws in gameplay: thin, short, bright at
    // the head and gone by the tail. It is one strip of geometry, and it is the
    // entire reason a thin dark stick at forty metres reads as a shot at all.
    private void AddTrail(GameObject go)
    {
        var tr = go.GetComponentInChildren<TrailRenderer>();
        if (tr == null) tr = go.AddComponent<TrailRenderer>();

        tr.time = arrowTrailTime;
        tr.startWidth = arrowTrailWidth;
        tr.endWidth = 0f;
        tr.numCapVertices = 2;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;
        tr.alignment = LineAlignment.View;

        // A TrailRenderer created from script has NO material and renders
        // magenta — the same trap the statue shot's line renderers fell into.
        if (tr.sharedMaterial == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh != null)
            {
                var m = new Material(sh);
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
                if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                tr.material = m;
            }
        }

        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(arrowTrailColor, 0f), new GradientColorKey(arrowTrailColor, 1f) },
            new[] { new GradientAlphaKey(arrowTrailColor.a, 0f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = grad;
        tr.Clear();
        tr.emitting = false;
    }

    // Both sides loose at once, from BEHIND their own lines, so the two flights
    // cross over the gap. That crossing is the single image a low side-on camera
    // standing in that gap exists to catch.
    private IEnumerator Volley()
    {
        if (arrowPool.Count == 0) yield break;

        float wait = volleyStagger / Mathf.Max(1, arrowsPerVolley);
        for (int i = 0; i < arrowsPerVolley; i++)
        {
            LaunchOne(-axis, axis);     // the player's side, loosing at the legion
            LaunchOne(axis, -axis);     // and back the other way

            // Spread over a moment. All at once is a firework; staggered, archers.
            yield return new WaitForSecondsRealtime(wait);
        }
    }

    // `fromSide` is which half the volley leaves from, `towards` where it goes.
    private void LaunchOne(Vector3 fromSide, Vector3 towards)
    {
        if (nextArrow >= arrowPool.Count) return;
        int index = nextArrow++;
        Transform t = arrowPool[index];
        if (t == null) return;

        Vector3 front = centre + fromSide * (gap * 0.5f);
        Vector3 launch = front + fromSide * arrowLaunchBehind
                       + side * Random.Range(-arrowSpread * 0.5f, arrowSpread * 0.5f);
        launch.y = Ground(launch) + arrowLaunchHeight;

        Vector3 land = centre + towards * Random.Range(gap * 0.15f, gap * 0.55f)
                     + side * Random.Range(-arrowSpread * 0.5f, arrowSpread * 0.5f);
        land.y = Ground(land);

        // Solve the launch velocity for a fixed flight time, so every arrow in a
        // volley lands in the same window however far it has to travel — which is
        // what makes a volley read as one order rather than as scattered shots.
        float flight = Mathf.Max(0.2f, arrowFlightSeconds * Random.Range(0.9f, 1.1f));
        Vector3 d = land - launch;
        Vector3 v = new Vector3(d.x / flight, d.y / flight + 0.5f * arrowGravity * flight, d.z / flight);

        t.localScale = arrowScale[index];
        t.position = launch;
        t.rotation = Quaternion.LookRotation(v.normalized);

        var tr = t.GetComponentInChildren<TrailRenderer>();
        if (tr != null) { tr.Clear(); tr.emitting = true; }

        arrowsInFlight.Add(new Arrow { t = t, velocity = v, life = flight + 0.35f });
    }

    private void DriveArrows()
    {
        float dt = Time.unscaledDeltaTime;
        for (int i = arrowsInFlight.Count - 1; i >= 0; i--)
        {
            Arrow a = arrowsInFlight[i];
            if (a.t == null) { arrowsInFlight.RemoveAt(i); continue; }

            a.velocity += Vector3.down * (arrowGravity * dt);
            a.t.position += a.velocity * dt;
            // Tilting to follow the trajectory is most of what makes an arrow
            // read as an arrow rather than as a stick sliding through the air.
            if (a.velocity.sqrMagnitude > 0.001f) a.t.rotation = Quaternion.LookRotation(a.velocity.normalized);

            a.life -= dt;
            if (a.life > 0f && a.t.position.y > Ground(a.t.position) - 0.2f) continue;

            var tr = a.t.GetComponentInChildren<TrailRenderer>();
            if (tr != null) tr.emitting = false;
            a.t.localScale = Vector3.one * 0.0001f;     // gone, without a destroy on the beat
            arrowsInFlight.RemoveAt(i);
        }
    }

    private float Ground(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 80f, Vector3.down, out RaycastHit hit, 300f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point.y;

        Terrain[] all = Terrain.activeTerrains;
        if (all != null)
        {
            foreach (var terr in all)
            {
                if (terr == null || terr.terrainData == null) continue;
                Vector3 o = terr.transform.position, sz = terr.terrainData.size;
                if (p.x >= o.x && p.x <= o.x + sz.x && p.z >= o.z && p.z <= o.z + sz.z)
                    return terr.SampleHeight(p) + o.y;
            }
        }
        return centre.y;
    }

    // ---- fog -----------------------------------------------------------------

    // Pure Volumetric Fog lives in its own assembly, so a direct reference would
    // stop this file compiling for anyone who removes the package. Reached by
    // name and written through reflection, the same way DayNightCycle does it.
    private Component Fog()
    {
        System.Type ty = System.Type.GetType("BKPureNature.PureVolumetricFog, BKPureNature.PureVolumetricFog");
        return ty != null ? Object.FindFirstObjectByType(ty) as Component : null;
    }

    private float fogDensity0 = -1f, fogHeight0 = -1f, fogFloor0;

    private IEnumerator EaseFog()
    {
        Component fog = Fog();
        if (fog == null) yield break;

        FieldInfo dens = Field(fog, "density");
        FieldInfo hgt = Field(fog, "groundFogHeight");
        FieldInfo floor = Field(fog, "noiseFloor");
        if (dens == null || hgt == null) yield break;

        fogDensity0 = (float)dens.GetValue(fog);
        fogHeight0 = (float)hgt.GetValue(fog);
        if (floor != null) fogFloor0 = (float)floor.GetValue(fog);

        float t = 0f;
        while (t < fogEaseSeconds)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.01f, fogEaseSeconds));
            dens.SetValue(fog, Mathf.Lerp(fogDensity0, clashFogDensity, k));
            hgt.SetValue(fog, Mathf.Lerp(fogHeight0, clashFogHeight, k));
            // ==== THINNING IT IS NOT THE SAME AS KEEPING IT ====
            //
            // The noise only ever REMOVES density, and with the floor at zero it
            // is free to remove all of it. At march density the holes never show
            // because even a carved patch is still thick; thinned to a third of
            // that, a hole is genuinely clear air, and a shot filmed straight
            // through one looks as though the fog were switched off rather than
            // reduced. Raising the floor is what turns "less fog" into "thinner
            // fog" — the same air everywhere, just less of it.
            if (floor != null) floor.SetValue(fog, Mathf.Lerp(fogFloor0, clashNoiseFloor, k));
            yield return null;
        }
        dens.SetValue(fog, clashFogDensity);
        hgt.SetValue(fog, clashFogHeight);
        if (floor != null) floor.SetValue(fog, clashNoiseFloor);
    }

    // Play Mode reloads the scene, so this is only good manners — but a shot that
    // leaves the world changed behind it is a shot nobody can run twice.
    private void RestoreFog()
    {
        if (fogDensity0 < 0f) return;
        Component fog = Fog();
        if (fog == null) return;

        FieldInfo dens = Field(fog, "density");
        FieldInfo hgt = Field(fog, "groundFogHeight");
        FieldInfo floor = Field(fog, "noiseFloor");
        if (dens != null) dens.SetValue(fog, fogDensity0);
        if (hgt != null) hgt.SetValue(fog, fogHeight0);
        if (floor != null) floor.SetValue(fog, fogFloor0);
    }

    // Its settings are [SerializeField] private, so NonPublic is not optional.
    private static FieldInfo Field(Component fog, string name)
    {
        return fog.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    // ---- weather -------------------------------------------------------------

    private void BuildRain()
    {
        if (rainPrefab == null || shotCamera == null) return;

        var go = Instantiate(rainPrefab);
        go.name = "Clash_Rain";
        var follow = go.GetComponent<TrailerRainFollow>();
        if (follow == null) follow = go.AddComponent<TrailerRainFollow>();
        follow.target = shotCamera.transform;
        follow.height = rainHeight;
        // The ground splashes read as specks skittering over the earth at this
        // scale rather than as water, the same as they did on the ride.
        follow.splashes = false;
    }

    // ---- camera --------------------------------------------------------------

    private void PlaceCamera(float k)
    {
        if (shotCamera == null) return;

        // In the gap, to one side, at the height of the men in it. Both armies
        // enter from opposite edges and close across the middle of frame, which
        // is the only framing in which an audience reads "two sides" rather than
        // "a crowd".
        // Eased at both ends, so the push has no start and no stop — a linear
        // dolly announces itself at both, and the one thing this move must not
        // do is be noticed.
        float e = k * k * (3f - 2f * k);

        Vector3 pos = centre + side * (cameraSide - cameraCreep * e);
        // Sit it on whatever ground is actually under it and hold its height
        // above that — on a slope a fixed world height either buries the lens or
        // leaves it floating.
        pos.y = Ground(pos) + Mathf.Lerp(cameraHeight, cameraEndHeight, e);

        Vector3 aim = centre;
        aim.y = Ground(centre) + aimHeight;

        float amp = handheld * (0.6f + 0.9f * k);
        float tt = Time.unscaledTime;
        Vector3 shake = new Vector3(Mathf.PerlinNoise(tt * 1.3f, 0f) - 0.5f,
                                    Mathf.PerlinNoise(0f, tt * 1.9f) - 0.5f,
                                    Mathf.PerlinNoise(tt * 1.1f, 7f) - 0.5f) * (amp * 2f);

        shotCamera.transform.position = pos + shake;
        shotCamera.transform.rotation = Quaternion.LookRotation((aim - pos).normalized);
        shotCamera.fieldOfView = Mathf.Lerp(cameraFov, cameraEndFov, e);
    }

    // ---- audio ---------------------------------------------------------------

    private readonly List<string> beds = new List<string>();

    private void Cue(string id)
    {
        if (AudioManager.Instance == null || string.IsNullOrEmpty(id)) return;
        AudioManager.Instance.PlaySFX(id);
    }

    private void Cue3D(string id, Vector3 at)
    {
        if (AudioManager.Instance == null || string.IsNullOrEmpty(id)) return;
        AudioManager.Instance.PlaySFX3D(id, at);
    }

    private void Loop(string id)
    {
        if (AudioManager.Instance == null || string.IsNullOrEmpty(id)) return;
        AudioManager.Instance.PlaySFX(id);
        beds.Add(id);
    }

    // Everything stops on the cut. A wind bed still running under the black is
    // the clearest way to tell an audience the shot merely stopped.
    private void DropOut()
    {
        if (AudioManager.Instance == null) return;
        for (int i = 0; i < beds.Count; i++) AudioManager.Instance.StopLoopedBed(beds[i]);
        beds.Clear();
    }
}
