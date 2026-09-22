using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.Playables;
using Unity.Cinemachine;

// Master director that plays the whole trailer in ONE Play:
//   PHASE 1   — the Act I ride on spline 1, filmed by the Act I Timeline shots.
//               The world stays SUMMER here (no season change on camera).
//   TIMELAPSE — near the end of the ride the camera cranes UP and looks out over
//               the REGION (it does not follow the horse) while the sun races
//               through several days and the world turns autumn -> winter. The
//               horse keeps galloping away, so it never "runs on the spot".
//   PHASE 2   — hard CUT to spline_p3: the horse is already running and the
//               Part 2 rig (low, fearful angles / lightning / rear / fall) plays.
//
// Put it on its OWN GameObject (not a rig it toggles). It auto-finds everything.
public class TrailerSequenceDirector : MonoBehaviour
{
    [Header("Auto-found if left empty")]
    public TrailerHorseRide ride;
    public SplineContainer part1Spline;      // the road (Part 1)
    public SplineContainer part2Spline;      // spline_p3
    public GameObject actIRig;               // LoreTrailer_Rig
    public GameObject part2Rig;              // LoreTrailer_Part2_Rig
    public TrailerSeasonRide season;
    public TrailerTerrainSeasons terrainSeason;

    [Header("Timing")]
    [Tooltip("Seconds for the horse to ride the WHOLE Part 1 spline. Must match what the Act I Timeline shots were cut for (24s) — a fixed m/s broke the shot timing.")]
    public float part1RideSeconds = 24f;
    [Tooltip("Seconds to ride the whole Part 2 spline.")]
    public float part2RideSeconds = 22f;
    [Tooltip("Where Part 1 hands over to the crane. Act I's own CM_04 crane shot starts around 0.8, so we take over BEFORE it — otherwise there are two cranes and CM_04 sits in the ground aiming at the horse.")]
    [Range(0f, 1f)] public float part1EndProgress = 0.78f;
    public float timelapseSeconds = 7f;
    public float dayNightCyclesInTimelapse = 4f;

    [Header("Crane (region reveal — does NOT follow the horse)")]
    public float craneStartHeight = 10f;
    public float craneEndHeight = 48f;
    public float craneStartPitch = 25f;
    public float craneEndPitch = 55f;
    [Tooltip("Metres BEHIND the horse's last position to anchor the crane, so it looks out over the region the rider is heading into rather than straight down at him.")]
    public float craneSetBack = 22f;

    [Header("Crane feel")]
    [Tooltip("Field of view at the start and end of the rise. Widening as it climbs makes the land seem to open out; a fixed lens makes the same move read as a lift.")]
    public float craneStartFov = 42f;
    public float craneEndFov = 62f;
    [Tooltip("Slow drift in yaw across the reveal, in degrees. A crane that only goes straight up looks mechanical; a few degrees of turn makes it feel operated.")]
    public float craneYawDrift = 7f;

    [Header("Where the shot ends")]
    // ==== THE RIDE IS ONE SHOT, NOT TWO ====
    //
    // This director was written to play the whole trailer end to end, so after
    // the crane reveal it cut to spline_p3 and ran PART 2 — the rider thrown,
    // the fall, the skeletons. That is a separate beat with its own cut, and
    // nobody watching "the ride through the forest" asked for it: the shot is
    // the gallop and the crane lifting off it, and it should hand over there.
    //
    // With this on, Part 2 is not merely skipped at the end — its rig is never
    // brought up at all, so the terrain it carries is never activated and the
    // cost that used to buy the hand-off is not paid either.
    [Tooltip("End the shot on the crane reveal instead of cutting to Part 2 (the fall and the skeletons). Set by the Lore Trailer launcher for the ride shot.")]
    public bool endAfterCrane;
    [Tooltip("Seconds the crane holds on the region after the rise finishes, before the shot fades out. Only used when End After Crane is on.")]
    public float craneHoldSeconds = 1.6f;
    [Tooltip("Seconds of fade to black that close the shot. Only used when End After Crane is on.")]
    public float craneOutFade = 1.2f;

    // ==== THE WORLD DOES NOT TURN DURING A ONE-SHOT RIDE ====
    //
    // The time-lapse was built to carry the whole trailer from Act I to Act II,
    // so it ran summer into winter under the crane: the terrain texture swapped,
    // TrailerSeasonRide re-tinted every tree and swapped the birch materials for
    // their autumn and winter versions, and the sun span through four days.
    //
    // As the closing move of a single ride that is not a time-lapse, it is a
    // costume change happening behind the actor. The crane is the shot; the
    // land should be the same land it was galloping through a second earlier.
    [Tooltip("Hold the season, the tree colours and the sun where they started instead of turning the world under the crane. Set by the Lore Trailer launcher for the ride shot.")]
    public bool holdWorld;

    public bool IsFinished { get; private set; }

    [Header("Horse hand-off")]
    [Tooltip("Seconds into the time-lapse after which the horse is hidden and moved to spline_p3. Without this he visibly pops across the map while the crane is watching.")]
    public float hideHorseAfter = 2.2f;

    private enum Phase { Part1, Timelapse, Part2, Ending }
    private Phase _phase = Phase.Part1;
    private float _tlT;
    private CinemachineCamera _crane;
    private Vector3 _craneAnchor;
    private float _craneYaw, _sunYaw;
    private bool _horseParked;

    [Header("Diagnostics")]
    [Tooltip("Log which camera the brain is actually LIVE on during the hand-off. Deleting cameras by guesswork has not found the one sitting in the terrain; this names it.")]
    public bool logLiveCamera = true;
    private string _lastLiveCam;

    private void Start()
    {
        AutoFind();

        // Force the PART 1 starting state (Part 2 setup leaves the horse on
        // spline_p3 and the Act I rig disabled — that's why it "started at Part 2").
        if (actIRig != null)
        {
            actIRig.SetActive(true);
            // The rig is saved parked, so its Timeline is not running when we
            // switch it on — start it explicitly or Act I has no shots at all.
            var dir = actIRig.GetComponent<PlayableDirector>();
            if (dir != null) { dir.enabled = true; dir.time = 0d; dir.Play(); }
        }
        else Debug.LogWarning("[Trailer] No 'LoreTrailer_Rig' in the scene — run 'Setup Act I Road Ride'.");

        ParkUnusedActICameras();

        // ==== ACTIVATING A TERRAIN MID-TRAILER IS THE HANG ====
        //
        // The Part 2 rig was switched off here and switched back on at the
        // hand-over, right after the statue breaks. That rig contains a Terrain,
        // and ACTIVATING a terrain makes Unity rebuild its tree and detail
        // render data synchronously — a multi-second stall landing exactly on
        // the cut, which from the outside is indistinguishable from the engine
        // hanging. TrailerShotChain already learned this and says so at length
        // at the top of that file; this director was still doing it.
        //
        // So the rig comes up ONCE, here, during the opening black where a
        // stall costs nothing, and stays up. Its CAMERAS are what get parked —
        // the set itself sits far from Part 1 and outside the live frustum, so
        // leaving it standing costs nothing to render.
        //
        // Duplicate rigs from earlier tool runs are still deactivated outright:
        // those are not going to be filmed and paying for their terrain would be
        // the same mistake for no reason.
        if (endAfterCrane)
        {
            // Nothing from Part 2 is filmed, so nothing from Part 2 is paid for.
            foreach (var g in TrailerFind.AllByName("LoreTrailer_Part2_Rig")) g.SetActive(false);
            part2Rig = null;
            part2Spline = null;
        }
        else
        {
            foreach (var g in TrailerFind.AllByName("LoreTrailer_Part2_Rig"))
                if (g != part2Rig) g.SetActive(false);

            if (part2Rig != null)
            {
                part2Rig.SetActive(true);
                ParkRigCameras(part2Rig);
            }
        }

        // Seasons are OURS from the start and HELD at summer, so the world never
        // changes while the Part 1 cameras are still filming the ride.
        if (season != null) { season.manual = true; season.ApplyU(0f); }
        if (terrainSeason != null) { terrainSeason.manual = true; terrainSeason.ApplyU(0f); }

        if (ride != null && part1Spline != null)
        {
            ride.path = part1Spline;
            ride.autoFitSeconds = part1RideSeconds;   // time-based, matches the shot timing
            ride.progress01 = 0f;
            ride.enabled = true;
            ride.BeginRide();
        }
        BuildCrane();

        // Open on black behind letterbox bars, then fade up into the ride. A
        // trailer that simply cuts to gameplay reads as a preview; a trailer that
        // opens reads as a film, and this costs nothing structurally.
        TrailerCinematicPolish.GetOrCreate().OpenTrailer();
    }

    [Header("Act I cameras")]
    [Tooltip("The ONLY Act I cameras this trailer uses. Everything else on the rig is parked.\n\nThe rig also carries CM_05..CM_13, staged for later acts. Those have Spline Dolly components with no spline assigned, so they sit at the world origin — inside the terrain — and any of them going live is the camera that kept appearing in the ground. CM_04 is excluded too: it is Act I's own crane, and this director owns the end crane.")]
    public string[] keepActICameras = { "CM_01", "CM_02", "CM_03" };

    private void ParkUnusedActICameras()
    {
        if (actIRig == null || keepActICameras == null) return;
        int parked = 0;
        foreach (var cam in actIRig.GetComponentsInChildren<CinemachineCamera>(true))
        {
            if (cam == null) continue;

            bool keep = false;
            foreach (var n in keepActICameras)
                if (!string.IsNullOrEmpty(n) && cam.name.Contains(n)) { keep = true; break; }
            if (keep) continue;

            // Disabled, not destroyed: these are staged for later acts and the
            // scene should keep them. A disabled camera cannot be made live by a
            // Timeline track either, which priority alone could not prevent.
            cam.gameObject.SetActive(false);
            parked++;
        }
        if (parked > 0) Debug.Log($"[Trailer] Parked {parked} unused Act I camera(s) — only [{string.Join(", ", keepActICameras)}] film Part 1.");
    }

    private void AutoFind()
    {
        if (ride == null) ride = Object.FindFirstObjectByType<TrailerHorseRide>();
        // TrailerFind, not GameObject.Find: the rigs are parked (disabled) between
        // phases and Find never returns disabled objects, so both came back null
        // and nothing was ever switched on.
        if (actIRig == null) actIRig = TrailerFind.ByName("LoreTrailer_Rig");
        if (part2Rig == null) part2Rig = TrailerFind.ByName("LoreTrailer_Part2_Rig");
        if (season == null && actIRig != null) season = actIRig.GetComponent<TrailerSeasonRide>();
        if (season == null) season = Object.FindFirstObjectByType<TrailerSeasonRide>();
        if (terrainSeason == null) terrainSeason = Object.FindFirstObjectByType<TrailerTerrainSeasons>();

        var splines = Object.FindObjectsByType<SplineContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (part2Spline == null)
            part2Spline = splines.FirstOrDefault(s => { var n = s.name.ToLowerInvariant(); return n.Contains("p3") || n.Contains("part2") || n.Contains("actii"); });
        if (part1Spline == null)
            part1Spline = splines.FirstOrDefault(s => { var n = s.name.ToLowerInvariant(); return n.Contains("road") && s != part2Spline; })
                       ?? splines.FirstOrDefault(s => s != part2Spline);
    }

    // A bare camera we drive by transform — no follow/aim, so it frames the REGION
    // instead of chasing the horse.
    private void BuildCrane()
    {
        var go = new GameObject("CM_TimelapseCrane");
        go.transform.SetParent(transform, false);
        _crane = go.AddComponent<CinemachineCamera>();
        _crane.Lens.FieldOfView = 55f;
        var pr = _crane.Priority; pr.Value = 0; _crane.Priority = pr;
    }

    private void Update()
    {
        if (ride == null) return;

        switch (_phase)
        {
            case Phase.Part1:
                if (ride.progress01 >= part1EndProgress) BeginTimelapse();
                break;

            case Phase.Timelapse:
                _tlT += Time.deltaTime;
                ReportLiveCamera();
                // Once he has galloped away, hide him and move him onto spline_p3
                // OFF CAMERA, so he never pops across the map mid-shot.
                //
                // Only when there IS a Part 2. Hiding him exists to cover a
                // teleport; with the shot ending on the crane there is no
                // teleport to cover, and hiding him two seconds into a
                // seven-second rise simply deletes the rider out of the middle of
                // the reveal. He keeps riding instead — TrailerHorseRide overruns
                // past the last knot, so he gallops out of frame rather than
                // stopping on the spot.
                if (!endAfterCrane && !_horseParked && _tlT >= hideHorseAfter) ParkHorseForPart2();
                float f = timelapseSeconds > 0.01f ? Mathf.Clamp01(_tlT / timelapseSeconds) : 1f;
                DriveTimelapse(f);
                if (f >= 1f) { if (endAfterCrane) BeginCraneOut(); else BeginPart2(); }
                break;

            case Phase.Part2:
                if (season != null) { season.ApplyU(1f); HoldWinterSun(); }
                if (terrainSeason != null) terrainSeason.ApplyU(1f);
                break;

            case Phase.Ending:
                // Hold the framing the rise ended on. Without this the crane is
                // simply left wherever the last DriveTimelapse call put it and
                // any damping drifts it off the composition during the hold.
                PlaceCrane(1f);
                break;
        }
    }

    // The shot's own ending, for when there is no Part 2 to cut to: hold the
    // region for a beat so the reveal lands, then fade out. A reveal that cuts
    // the instant the crane stops reads as the footage running out.
    private void BeginCraneOut()
    {
        _phase = Phase.Ending;
        if (!holdWorld)
        {
            if (season != null) { season.ApplyU(1f); HoldWinterSun(); }
            if (terrainSeason != null) terrainSeason.ApplyU(1f);
        }
        StartCoroutine(CraneOutRoutine());
    }

    private IEnumerator CraneOutRoutine()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, craneHoldSeconds));

        var polish = TrailerCinematicPolish.Instance;
        if (polish != null) polish.FadeToBlack(craneOutFade);

        yield return new WaitForSecondsRealtime(craneOutFade);
        IsFinished = true;
    }

    // Names the camera the brain is live on, each time it changes. Whatever is
    // sitting in the terrain will identify itself here.
    // Blunt, and deliberately so. Naming the stray camera has failed three times
    // (CM_04, then CM_05_TabirOzhyvaye, then whatever was next), because the rig
    // carries a whole row of later-act cameras whose Spline Dolly has no spline
    // and which therefore sit at the world origin, inside the ground. Rather than
    // keep guessing which one wins, everything that is not the camera this phase
    // is supposed to be on gets switched off.
    // Park a rig's cameras without deactivating the rig, so its terrain, trees
    // and detail layers are already built and paid for long before the cut.
    // Audio listeners go with them: a second live listener is a real bug, and
    // the reason the rig used to be switched off wholesale.
    private void ParkRigCameras(GameObject rig)
    {
        if (rig == null) return;
        foreach (var cam in rig.GetComponentsInChildren<CinemachineCamera>(true)) cam.gameObject.SetActive(false);
        foreach (var lis in rig.GetComponentsInChildren<AudioListener>(true)) lis.enabled = false;
    }

    private void WakeRigCameras(GameObject rig)
    {
        if (rig == null) return;
        foreach (var cam in rig.GetComponentsInChildren<CinemachineCamera>(true)) cam.gameObject.SetActive(true);
    }

    private void SoloCameras(params GameObject[] keepRoots)
    {
        int off = 0;
        foreach (var cam in Object.FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (cam == null) continue;

            bool keep = false;
            foreach (var root in keepRoots)
            {
                if (root == null) continue;
                if (cam.gameObject == root || cam.transform.IsChildOf(root.transform)) { keep = true; break; }
            }
            if (keep) continue;

            cam.gameObject.SetActive(false);
            off++;
        }
        if (off > 0) Debug.Log($"[Trailer] Solo: switched off {off} camera(s) that are not part of this shot.");
    }

    private void ReportLiveCamera()
    {
        if (!logLiveCamera) return;
        var brain = Object.FindFirstObjectByType<CinemachineBrain>();
        var live = brain != null ? brain.ActiveVirtualCamera : null;
        string n = live != null ? live.Name : "<none>";
        if (n == _lastLiveCam) return;
        _lastLiveCam = n;

        var go = (live as MonoBehaviour) != null ? (live as MonoBehaviour).gameObject : null;
        string parent = go != null && go.transform.parent != null ? go.transform.parent.name : "<root>";
        Debug.Log($"[Trailer] LIVE camera is now '{n}' (parent '{parent}') at {(go != null ? go.transform.position.ToString() : "?")}");
    }

    private void BeginTimelapse()
    {
        _phase = Phase.Timelapse; _tlT = 0f;

        // NOTE: the ride is deliberately NOT stopped — the horse keeps galloping
        // away (overrun) so it never stands still running on the spot.
        StopActIDirector();
        LowerActICams();

        // Sit BEHIND the rider and look the way he was heading — the shot is the
        // REGION, not the horse.
        // Nothing but the crane films the reveal.
        SoloCameras(_crane != null ? _crane.gameObject : null);

        _craneYaw = ride.transform.eulerAngles.y;
        _craneAnchor = ride.transform.position - ride.transform.forward * craneSetBack;
        // Heights are measured from the ground UNDER THE CRANE, not from the
        // horse. Setting back 22m can easily land on higher ground — if the
        // horse has just come down a slope, "10m above the horse" is inside the
        // hill behind him, which is the camera that kept appearing in the
        // textures at the hand-off.
        if (TryGroundY(_craneAnchor, out float craneGroundY)) _craneAnchor.y = craneGroundY;
        else _craneAnchor.y = ride.transform.position.y;
        if (_crane != null)
        {
            var pr = _crane.Priority; pr.Value = 200; _crane.Priority = pr;
            PlaceCrane(0f);
            // The reveal is the quiet beat between two chases: let the score
            // settle rather than carrying the gallop's intensity into it.
            if (AudioManager.Instance != null) AudioManager.Instance.NotifyCombat(0f);
            // CUT to the crane. Blending would sweep the live camera across the
            // landscape to reach it, straight through whatever is in the way.
            var brain = Object.FindFirstObjectByType<CinemachineBrain>();
            if (brain != null)
                brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            _crane.PreviousStateIsValid = false;
            _crane.InternalUpdateCameraState(Vector3.up, -1f);
        }

        if (season != null && !holdWorld)
        {
            season.driveDayNight = false;                       // we spin the sun ourselves
            if (season.sun != null) _sunYaw = season.sun.transform.eulerAngles.y;
        }
    }

    private void DriveTimelapse(float f)
    {
        if (!holdWorld)
        {
            float su = Mathf.Lerp(0f, 1f, f);                   // summer -> winter across the timelapse
            if (season != null) season.ApplyU(su);
            if (terrainSeason != null) terrainSeason.ApplyU(su);

            // Several days race by.
            if (season != null && season.sun != null)
            {
                float pitch = 20f + f * dayNightCyclesInTimelapse * 360f;
                season.sun.transform.rotation = Quaternion.Euler(pitch, _sunYaw, 0f);
                float day = Mathf.Clamp01(Mathf.Sin(pitch * Mathf.Deg2Rad));
                season.sun.intensity = Mathf.Lerp(0.05f, 1.1f, day);
            }
        }

        PlaceCrane(f);
    }

    // Rise and tilt down over the landscape.
    private void PlaceCrane(float f)
    {
        if (_crane == null) return;

        // Ease in AND out. A linear rise starts and stops abruptly, which is the
        // difference between a camera move and a lift.
        float k = f * f * (3f - 2f * f);

        float h = Mathf.Lerp(craneStartHeight, craneEndHeight, k);
        float pitch = Mathf.Lerp(craneStartPitch, craneEndPitch, k);
        float yaw = _craneYaw + Mathf.Lerp(0f, craneYawDrift, k);

        _crane.transform.position = _craneAnchor + Vector3.up * h;
        _crane.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        // Widening as it climbs makes the land open out under the camera rather
        // than simply receding.
        var lens = _crane.Lens;
        lens.FieldOfView = Mathf.Lerp(craneStartFov, craneEndFov, k);
        _crane.Lens = lens;
    }

    private void BeginPart2()
    {
        _phase = Phase.Part2;

        // A breath of black over the hand-off. The cut is already hard; a single
        // dark frame under it turns a jump between two places into an edit.
        var polish = TrailerCinematicPolish.Instance;
        if (polish != null) { polish.FadeToBlack(0.12f); polish.FadeFromBlack(0.35f); }

        if (season != null) { season.ApplyU(1f); HoldWinterSun(); }
        if (terrainSeason != null) terrainSeason.ApplyU(1f);

        ParkHorseForPart2();      // no-op if the time-lapse already did it
        ShowHorse(true);

        LowerActICams();
        // HARD CUT. Three things are needed or the brain still glides in from the
        // old crane position across the map:
        //   1. the brain's default blend must be a CUT for this transition,
        //   2. the live crane must stop being a candidate,
        //   3. each Part 2 camera must be evaluated ONCE with damping disabled so
        //      it is already at its final spot on the very first frame.
        var brain = Object.FindFirstObjectByType<CinemachineBrain>();
        if (brain != null)
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);

        if (_crane != null) _crane.gameObject.SetActive(false);
        if (part2Rig != null)
        {
            // No SetActive here any more — see the note in Start. The rig has
            // been standing since the opening black; all that happens on the cut
            // is that its cameras wake up.
            WakeRigCameras(part2Rig);
            // Only Part 2's own cameras exist from here on.
            SoloCameras(part2Rig);
            SnapPart2Cameras();
        }
    }

    // Place every Part 2 camera at its final framing before the first frame is
    // rendered (deltaTime < 0 tells Cinemachine to skip all damping).
    private void SnapPart2Cameras()
    {
        foreach (var cam in part2Rig.GetComponentsInChildren<CinemachineCamera>(true))
        {
            cam.PreviousStateIsValid = false;
            cam.InternalUpdateCameraState(Vector3.up, -1f);
        }
    }

    // Hide the horse+rider and place them at the start of spline_p3, already
    // running. Called mid-time-lapse so the move is never on screen.
    private void ParkHorseForPart2()
    {
        if (_horseParked) return;
        _horseParked = true;

        ShowHorse(false);
        if (part2Spline != null)
        {
            ride.path = part2Spline;
            ride.autoFitSeconds = part2RideSeconds;
            ride.progress01 = 0f;
            ride.enabled = true;
            ride.BeginRide();
        }
    }

    private void ShowHorse(bool visible)
    {
        if (ride == null) return;
        foreach (var r in ride.GetComponentsInChildren<Renderer>(true))
            r.enabled = visible;
    }

    private static readonly string[] GroundNames = { "terrain", "ground", "floor", "road", "path" };

    private static bool TryGroundY(Vector3 pos, out float y)
    {
        y = pos.y;
        var hits = Physics.RaycastAll(pos + Vector3.up * 200f, Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity; bool found = false;
        foreach (var h in hits)
        {
            var col = h.collider; if (col == null) continue;
            bool g = col.GetComponentInParent<Terrain>() != null;
            if (!g) { string n = col.name.ToLowerInvariant(); foreach (var s in GroundNames) if (n.Contains(s)) { g = true; break; } }
            if (!g) continue;
            if (h.point.y > best) { best = h.point.y; found = true; }
        }
        if (found) { y = best; return true; }
        return false;
    }

    private void HoldWinterSun()
    {
        if (season == null || season.sun == null) return;
        season.sun.transform.rotation = Quaternion.Euler(18f, _sunYaw, 0f);
        season.sun.intensity = 0.8f;
    }

    private void StopActIDirector()
    {
        if (actIRig == null) return;
        var dir = actIRig.GetComponent<PlayableDirector>();
        if (dir != null) { dir.Stop(); dir.enabled = false; }
    }

    // Drop every Act I virtual camera's priority so none stays live at its old spot.
    private void LowerActICams()
    {
        if (actIRig == null) return;
        foreach (var cam in actIRig.GetComponentsInChildren<CinemachineCamera>(true))
        {
            var pr = cam.Priority; pr.Value = -100; cam.Priority = pr;
        }
    }
}
