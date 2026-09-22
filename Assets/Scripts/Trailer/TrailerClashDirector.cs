using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Trailer, the shot after the march — two armies close head-on and the frame
// goes black on contact.
//
// ==== WHY THIS AND NOT THE STAGED FIGHT ====
//
// TrailerBattleDirector blocks a duel: a ring, a counter, a knockdown, a rise.
// It is a good beat and an expensive one — every one of those moments has to be
// choreographed, timed and shot, and any of them landing badly reads as a bad
// fight rather than as a bad shot.
//
// This is the opposite trade. One idea, one camera position, no choreography:
// the player's army on one side, the legion from the march on the other, the
// lens side-on, and the two lines walking into each other. It says the whole
// thing the fight was going to say — two sides, one meeting — in a shape that
// cannot be performed badly, because nobody performs anything. And it costs
// nothing new: both armies, the terrain, the storm and the fog are already in
// this scene.
//
// The cut is the point. You do not show the collision; you take the screen away
// at the instant of it. What the audience imagines is worse than anything that
// could be animated, and it is the only ending that does not require the fight
// to be good.
//
// ==== WHY THE GAP IS THE STATE, NOT THE POSITIONS ====
//
// The obvious build gives every soldier a velocity and lets them walk. Two
// hundred units each integrating their own position drift apart: rounding,
// frame timing and ground snapping accumulate differently per unit, so the two
// front ranks arrive at slightly different times and the "instant of contact"
// is a smear that moves every time the shot is run.
//
// So there is ONE number — the gap between the two front ranks — and every unit
// in both armies is placed from it each frame. The lines meet exactly where the
// centre is and exactly when the gap reaches impactGap, take after take.
[DisallowMultipleComponent]
public class TrailerClashDirector : MonoBehaviour
{
    [Header("Where")]
    [Tooltip("The point the two lines meet. Left empty, this object's own position is used.")]
    public Transform clashCentre;
    [Tooltip("The direction the ENEMY army faces — the player's army marches the other way. Only the horizontal part is used.")]
    public Vector3 advanceAxis = Vector3.forward;

    [Header("The player's army")]
    [Tooltip("The hero himself, standing out in front of his own line. Left empty, the line closes up and nobody leads it.")]
    public GameObject playerPrefab;
    [Tooltip("Metres in front of his own front rank. Far enough to read as leading, close enough to still belong to the line.")]
    public float playerLead = 3.4f;
    [Tooltip("Barracks units. Knight / Barbarian / Rogue_Hooded are the three the game hires.")]
    public GameObject[] allyPrefabs;
    public int allyCount = 48;
    public int allyRanks = 6;

    [Header("The legion")]
    [Tooltip("The same skeleton the march uses.")]
    public GameObject enemyPrefab;
    [Tooltip("Deliberately more of them. The shot is not a fair fight, it is a last stand.")]
    public int enemyCount = 96;
    public int enemyRanks = 8;

    [Header("Formation")]
    public float fileSpacing = 1.6f;
    public float rankSpacing = 1.9f;
    [Tooltip("Metres of scatter on each unit's place in the grid. Zero is a chessboard; this is what makes it an army.")]
    public float positionJitter = 0.4f;
    [Tooltip("Metres between the two FRONT ranks when the shot opens.")]
    public float startGap = 62f;

    [Header("The advance")]
    [Tooltip("Both lines stand still for this long first. The stillness is what makes the first step land.")]
    public float holdSeconds = 2.4f;
    [Tooltip("Metres per second, per side, while they walk. The gap closes at twice this.")]
    public float walkSpeed = 1.7f;
    [Tooltip("Metres per second, per side, once they break into a charge.")]
    public float chargeSpeed = 5.4f;
    [Tooltip("The gap at which the walk becomes a charge.")]
    public float chargeAtGap = 30f;
    [Tooltip("Seconds the change of pace takes. Instant is a glitch; this is a decision.")]
    public float chargeRampSeconds = 1.1f;
    [Tooltip("The gap at which the frame goes black. Not zero — the cut has to land BEFORE anyone interpenetrates, because nothing here is animated to collide.")]
    public float impactGap = 2.6f;

    [Header("Camera")]
    public Camera shotCamera;
    [Tooltip("Metres to the side of the line the armies close along. This is the whole shot: side-on, so both armies enter frame from opposite edges.")]
    public float cameraSide = 30f;
    public float cameraHeight = 5.5f;
    [Tooltip("A long lens. It flattens the distance between the two lines, so they read as closing on each other rather than as two crowds in a field.")]
    public float cameraFov = 32f;
    [Tooltip("Metres the camera drifts in across the shot. Small — it should feel planted, not operated.")]
    public float cameraCreep = 3.5f;
    [Tooltip("Handheld amplitude in metres. This is a camera on sticks with somebody's hand on it, not a helmet cam.")]
    public float handheld = 0.022f;
    [Tooltip("Aim height above the ground at the meeting point.")]
    public float aimHeight = 1.6f;

    [Header("Weather")]
    // The scene's own Directional Light is already the storm the march is in —
    // white at 0.14, which is a dim wet night — so nothing here touches the
    // lighting. The rain is not in the scene though: the march director spawns
    // it, and the march director is switched off for this shot.
    [Tooltip("The same Heavy Rain prefab the march uses. Parented to the camera through TrailerRainFollow so it fills frame wherever the lens is.")]
    public GameObject rainPrefab;
    public float rainHeight = 12f;

    [Header("Out")]
    [Tooltip("Seconds of held black after the cut, so the shot has an out point.")]
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
        public Vector3 offset;      // file (x) and rank (z) within its own formation, plus jitter
        public int snapPhase;       // which frame in the stagger this one grounds on
    }

    private readonly List<Unit> allies = new List<Unit>();
    private readonly List<Unit> enemies = new List<Unit>();
    private Vector3 centre, axis, side;
    private float gap;
    private float speed;
    private int frame;

    // Grounding every unit every frame is a raycast per soldier per frame, and
    // there are the better part of two hundred of them. Spread across four
    // frames the error is a couple of centimetres of height on a figure that is
    // walking in a straight line, which nothing can see, for a quarter of the
    // cost. The march director learned the same thing with three hundred.
    private const int GroundStride = 4;

    private void Start()
    {
        if (autoPlay) Play();
    }

    public void Play()
    {
        if (IsFinished) return;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        TrailerLogGuard.Arm();

        centre = clashCentre != null ? clashCentre.position : transform.position;
        axis = advanceAxis; axis.y = 0f;
        axis = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.forward;
        // Perpendicular, on the horizontal plane. This is where the camera goes
        // and which way the ranks spread.
        side = new Vector3(axis.z, 0f, -axis.x);

        if (shotCamera == null) shotCamera = Camera.main;

        BuildArmy(allies, allyPrefabs, allyCount, allyRanks, true);
        BuildArmy(enemies, enemyPrefab != null ? new[] { enemyPrefab } : null, enemyCount, enemyRanks, false);
        BuildPlayer();

        gap = Mathf.Max(startGap, impactGap + 1f);
        speed = 0f;
        PlaceEverything(true);
        PlaceCamera(0f);

        BuildRain();

        var polish = TrailerCinematicPolish.GetOrCreate();
        polish.OpenTrailer();
        TrailerAudio.SilenceStaleBeds();
        Loop(windBed);

        // ---- 1. two lines, standing ----
        float t = 0f;
        while (t < holdSeconds)
        {
            t += Time.unscaledDeltaTime;
            PlaceCamera(Progress());
            yield return null;
        }

        // ---- 2. the advance ----
        Cue(hornCue);
        Loop(marchLoop);
        Cue3D(rattleCue, centre + axis * (gap * 0.5f));

        float rampT = 0f;
        bool charging = false;

        while (gap > impactGap)
        {
            float dt = Time.unscaledDeltaTime;

            if (!charging && gap <= chargeAtGap) charging = true;

            // Ease between the two paces rather than switching. An army that
            // changes speed on one frame reads as a playback rate, not a charge.
            float target = charging ? chargeSpeed : walkSpeed;
            rampT = Mathf.MoveTowards(rampT, charging ? 1f : 0f, dt / Mathf.Max(0.05f, chargeRampSeconds));
            speed = Mathf.Lerp(walkSpeed, target, charging ? rampT * rampT : 0f);

            // Both sides close, so the gap shuts at twice one side's speed.
            gap = Mathf.Max(impactGap, gap - speed * 2f * dt);

            PlaceEverything(false);
            PlaceCamera(Progress());
            yield return null;
        }

        // ---- 3. the cut ----
        //
        // The sound goes one frame early on purpose. A hit that starts on the
        // same frame as the black reads as the video file ending; a hit that
        // starts a frame before it carries ACROSS the cut, and the black becomes
        // part of the impact rather than the absence of one.
        Cue3D(impactCue, centre + Vector3.up * aimHeight);
        yield return null;

        polish.SetFlash(Color.black);
        DropOut();

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, holdBlack));
        IsFinished = true;
    }

    private float Progress()
    {
        float span = Mathf.Max(1f, startGap - impactGap);
        return Mathf.Clamp01((startGap - gap) / span);
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

            var u = new Unit
            {
                offset = new Vector3(across + Random.Range(-positionJitter, positionJitter),
                                     0f,
                                     back + Random.Range(-positionJitter, positionJitter)),
                snapPhase = i % GroundStride,
            };

            Vector3 facing = ally ? axis : -axis;
            u.puppet = TrailerPuppet.Spawn(prefab, centre, Quaternion.LookRotation(facing), transform);
            if (u.puppet == null) continue;

            into.Add(u);
        }
    }

    private Unit player;

    private void BuildPlayer()
    {
        if (playerPrefab == null) return;

        player = new Unit
        {
            offset = new Vector3(0f, 0f, -playerLead),   // ahead of his own front rank
            snapPhase = 0,
        };
        player.puppet = TrailerPuppet.Spawn(playerPrefab, centre, Quaternion.LookRotation(axis), transform);
        if (player.puppet == null) { player = null; return; }

        GameObject go = player.puppet.gameObject;

        // ==== THE HERO IS NOT AN ENEMY, SO Strip DOES NOT COVER HIM ====
        //
        // TrailerPuppet.Strip knows about EnemyAI, agents and health canvases.
        // The player prefab has none of those and everything else instead:
        // PlayerController reads input and would walk him out of the shot,
        // PlayerSpawnManager would try to place him at a spawn point, and the
        // CharacterController makes him solid enough to shove his own front rank
        // out of formation. DestroyImmediate, not disable, for the same reason
        // Strip uses it — a component queued for destruction still gets its
        // Start, and Start is where each of these does the thing.
        foreach (var c in go.GetComponentsInChildren<PlayerController>(true)) if (c != null) DestroyImmediate(c);
        foreach (var c in go.GetComponentsInChildren<PlayerSpawnManager>(true)) if (c != null) DestroyImmediate(c);
        foreach (var c in go.GetComponentsInChildren<CharacterController>(true)) if (c != null) c.enabled = false;

        // He is the one figure the shot is composed around, and he is a modular
        // rig whose skinned bounds come off a root bone rather than the pose.
        // Everyone else is one of a crowd and can afford to be wrong.
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr != null) smr.updateWhenOffscreen = true;
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
            player.puppet.SetGait(speed);
        }
    }

    // `back` is the direction the formation extends away from its own front
    // rank; `facing` is where its soldiers look.
    private void Place(List<Unit> army, Vector3 front, Vector3 back, Vector3 facing, bool groundAll)
    {
        for (int i = 0; i < army.Count; i++)
        {
            Unit u = army[i];
            if (u.puppet == null) continue;

            Vector3 p = front + side * u.offset.x + back * u.offset.z;
            bool ground = groundAll || (frame % GroundStride) == u.snapPhase;
            u.puppet.Place(p, facing, ground);
            u.puppet.SetGait(speed);
        }
    }

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

        // Side-on and planted. Both armies enter frame from opposite edges and
        // close across the middle of it, which is the only framing in which the
        // audience reads "two sides" rather than "a crowd".
        Vector3 aim = centre + Vector3.up * aimHeight;
        Vector3 pos = centre + side * (cameraSide - cameraCreep * k) + Vector3.up * cameraHeight;

        // Sit the camera on whatever ground is actually under it, then hold its
        // height above that — on a slope a fixed world height either buries the
        // lens or leaves it floating.
        if (Physics.Raycast(pos + Vector3.up * 80f, Vector3.down, out RaycastHit hit, 300f, ~0, QueryTriggerInteraction.Ignore))
            pos.y = hit.point.y + cameraHeight;

        // Perlin at incommensurate rates per axis, so it never visibly repeats,
        // and it grows a little as the lines close.
        float amp = handheld * (0.6f + 0.8f * k);
        float tt = Time.unscaledTime;
        Vector3 shake = new Vector3(Mathf.PerlinNoise(tt * 1.3f, 0f) - 0.5f,
                                    Mathf.PerlinNoise(0f, tt * 1.9f) - 0.5f,
                                    Mathf.PerlinNoise(tt * 1.1f, 7f) - 0.5f) * (amp * 2f);

        shotCamera.transform.position = pos + shake;
        shotCamera.transform.rotation = Quaternion.LookRotation((aim - pos).normalized);
        shotCamera.fieldOfView = cameraFov;
    }

    // ---- audio ---------------------------------------------------------------

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

    private readonly List<string> beds = new List<string>();

    // Everything stops on the cut. A wind bed still running under the black is
    // the single clearest way to tell an audience the shot merely stopped.
    private void DropOut()
    {
        if (AudioManager.Instance == null) return;
        for (int i = 0; i < beds.Count; i++) AudioManager.Instance.StopLoopedBed(beds[i]);
        beds.Clear();
    }
}
