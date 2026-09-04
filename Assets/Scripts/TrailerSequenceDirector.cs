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

    [Header("Horse hand-off")]
    [Tooltip("Seconds into the time-lapse after which the horse is hidden and moved to spline_p3. Without this he visibly pops across the map while the crane is watching.")]
    public float hideHorseAfter = 2.2f;

    private enum Phase { Part1, Timelapse, Part2 }
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

        // Park EVERY Part 2 rig (earlier tool runs could leave duplicates behind).
        foreach (var g in TrailerFind.AllByName("LoreTrailer_Part2_Rig")) g.SetActive(false);
        if (part2Rig != null) part2Rig.SetActive(false);

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
                if (!_horseParked && _tlT >= hideHorseAfter) ParkHorseForPart2();
                float f = timelapseSeconds > 0.01f ? Mathf.Clamp01(_tlT / timelapseSeconds) : 1f;
                DriveTimelapse(f);
                if (f >= 1f) BeginPart2();
                break;

            case Phase.Part2:
                if (season != null) { season.ApplyU(1f); HoldWinterSun(); }
                if (terrainSeason != null) terrainSeason.ApplyU(1f);
                break;
        }
    }

    // Names the camera the brain is live on, each time it changes. Whatever is
    // sitting in the terrain will identify itself here.
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
            // CUT to the crane. Blending would sweep the live camera across the
            // landscape to reach it, straight through whatever is in the way.
            var brain = Object.FindFirstObjectByType<CinemachineBrain>();
            if (brain != null)
                brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            _crane.PreviousStateIsValid = false;
            _crane.InternalUpdateCameraState(Vector3.up, -1f);
        }

        if (season != null)
        {
            season.driveDayNight = false;                       // we spin the sun ourselves
            if (season.sun != null) _sunYaw = season.sun.transform.eulerAngles.y;
        }
    }

    private void DriveTimelapse(float f)
    {
        float su = Mathf.Lerp(0f, 1f, f);                       // summer -> winter across the timelapse
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

        PlaceCrane(f);
    }

    // Rise and tilt down over the landscape.
    private void PlaceCrane(float f)
    {
        if (_crane == null) return;
        float k = f * f * (3f - 2f * f);
        float h = Mathf.Lerp(craneStartHeight, craneEndHeight, k);
        float pitch = Mathf.Lerp(craneStartPitch, craneEndPitch, k);
        _crane.transform.position = _craneAnchor + Vector3.up * h;
        _crane.transform.rotation = Quaternion.Euler(pitch, _craneYaw, 0f);
    }

    private void BeginPart2()
    {
        _phase = Phase.Part2;

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
            part2Rig.SetActive(true);
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
