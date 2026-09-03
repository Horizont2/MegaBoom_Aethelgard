using System.Linq;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.Playables;
using Unity.Cinemachine;

// Master director that plays the whole trailer in ONE Play:
//   PHASE 1  — the Act I ride (spline 1), Act I cameras.
//   TIMELAPSE — when the ride nears its end, the camera cranes UP and holds while
//               the sun races through several days and the world turns autumn →
//               winter (recolour).
//   PHASE 2  — the horse + rider are moved to spline_p3 and gallop on through the
//               winter world; the Part 2 rig (cuts, lightning, rear, fall,
//               pursuing skeletons) takes over.
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
    public float part1Speed = 12f;

    [Header("Timing")]
    [Range(0f, 1f)] public float part1EndProgress = 0.9f;
    public float timelapseSeconds = 7f;
    public float dayNightCyclesInTimelapse = 4f;

    [Header("Crane hold")]
    public Vector3 craneStartOffset = new Vector3(0f, 7f, -9f);
    public Vector3 craneEndOffset = new Vector3(0f, 34f, -8f);   // high overhead, not far behind (avoids map-edge textures)

    private enum Phase { Part1, Timelapse, Part2 }
    private Phase _phase = Phase.Part1;
    private float _tlT;
    private CinemachineCamera _crane;
    private CinemachineFollow _craneFollow;
    private float _sunYaw;

    private void Start()
    {
        AutoFind();
        // Force the PART 1 starting state (Part 2 setup left the horse on spline_p3
        // and the Act I rig disabled — that's why it "started at Part 2").
        if (actIRig != null) actIRig.SetActive(true);
        if (part2Rig != null) part2Rig.SetActive(false);
        if (season != null) season.manual = false;
        if (ride != null && part1Spline != null)
        {
            ride.path = part1Spline;
            ride.autoFitSeconds = 0f;
            ride.speed = part1Speed;
            ride.progress01 = 0f;
            ride.enabled = true;
            ride.BeginRide();
        }
        BuildCrane();
    }

    private void AutoFind()
    {
        if (ride == null) ride = Object.FindFirstObjectByType<TrailerHorseRide>();
        if (actIRig == null) actIRig = GameObject.Find("LoreTrailer_Rig");
        if (part2Rig == null) part2Rig = GameObject.Find("LoreTrailer_Part2_Rig");
        if (season == null && actIRig != null) season = actIRig.GetComponent<TrailerSeasonRide>();
        if (season == null) season = Object.FindFirstObjectByType<TrailerSeasonRide>();
        var splines = Object.FindObjectsByType<SplineContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (part2Spline == null)
            part2Spline = splines.FirstOrDefault(s => { var n = s.name.ToLowerInvariant(); return n.Contains("p3") || n.Contains("part2") || n.Contains("actii"); });
        if (part1Spline == null)
            part1Spline = splines.FirstOrDefault(s => { var n = s.name.ToLowerInvariant(); return n.Contains("road") && s != part2Spline; })
                       ?? splines.FirstOrDefault(s => s != part2Spline);
    }

    private void BuildCrane()
    {
        var go = new GameObject("CM_TimelapseCrane");
        go.transform.SetParent(transform, false);
        _crane = go.AddComponent<CinemachineCamera>();
        _crane.Lens.FieldOfView = 46f;
        var pr = _crane.Priority; pr.Value = 0; _crane.Priority = pr;   // off until the timelapse
        if (ride != null) { var t = _crane.Target; t.TrackingTarget = ride.transform; _crane.Target = t; }
        _craneFollow = go.AddComponent<CinemachineFollow>();
        _craneFollow.FollowOffset = craneStartOffset;
        var ts = _craneFollow.TrackerSettings; ts.PositionDamping = new Vector3(1.5f, 1.5f, 1.8f); _craneFollow.TrackerSettings = ts;
        go.AddComponent<CinemachineRotationComposer>();
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
                float f = timelapseSeconds > 0.01f ? Mathf.Clamp01(_tlT / timelapseSeconds) : 1f;
                DriveTimelapse(f);
                if (f >= 1f) BeginPart2();
                break;

            case Phase.Part2:
                // Hold winter (manual season would otherwise let DayNightCycle back in).
                if (season != null) { season.ApplyU(1f); HoldWinterSun(); }
                break;
        }
    }

    private void BeginTimelapse()
    {
        _phase = Phase.Timelapse; _tlT = 0f;
        ride.enabled = false;                                   // horse holds (ride basically done)
        StopActIDirector();                                     // let the crane own the brain
        LowerActICams();                                        // stop a leftover Act I cam from staying live in the textures
        if (_crane != null) { var pr = _crane.Priority; pr.Value = 200; _crane.Priority = pr; }
        if (season != null)
        {
            season.manual = true;
            season.driveDayNight = false;                       // we spin the sun ourselves
            if (season.sun != null) _sunYaw = season.sun.transform.eulerAngles.y;
        }
    }

    private void DriveTimelapse(float f)
    {
        // Autumn -> deep winter recolour.
        if (season != null) season.ApplyU(Mathf.Lerp(0.55f, 1f, f));

        // Several days race by: spin the sun fast.
        if (season != null && season.sun != null)
        {
            float pitch = 20f + f * dayNightCyclesInTimelapse * 360f;
            season.sun.transform.rotation = Quaternion.Euler(pitch, _sunYaw, 0f);
            float day = Mathf.Clamp01(Mathf.Sin(pitch * Mathf.Deg2Rad));
            season.sun.intensity = Mathf.Lerp(0.05f, 1.1f, day);
        }

        // Crane rises as time passes.
        if (_craneFollow != null)
        {
            float k = f * f * (3f - 2f * f);
            _craneFollow.FollowOffset = Vector3.Lerp(craneStartOffset, craneEndOffset, k);
        }
    }

    private void BeginPart2()
    {
        _phase = Phase.Part2;

        // Hold winter.
        if (season != null) { season.ApplyU(1f); HoldWinterSun(); }

        // Move horse + rider (parented) to spline_p3 and ride on at a good pace.
        if (part2Spline != null)
        {
            ride.path = part2Spline;
            ride.autoFitSeconds = 0f;
            ride.speed = 12f;
            ride.progress01 = 0f;
            ride.enabled = true;
            ride.BeginRide();
        }
        LowerActICams();

        // Hand cameras to the Part 2 rig.
        if (_crane != null) { var pr = _crane.Priority; pr.Value = 0; _crane.Priority = pr; }
        if (part2Rig != null) part2Rig.SetActive(true);
        // Keep actIRig active so the season driver stays alive, but its director is
        // stopped and its cams are low priority — the Part 2 rig (priority 100) wins.
    }

    private void HoldWinterSun()
    {
        if (season == null || season.sun == null) return;
        // A low, cold winter sun.
        season.sun.transform.rotation = Quaternion.Euler(18f, _sunYaw, 0f);
        season.sun.intensity = 0.8f;
    }

    private void StopActIDirector()
    {
        if (actIRig == null) return;
        var dir = actIRig.GetComponent<PlayableDirector>();
        if (dir != null) { dir.Stop(); dir.enabled = false; }
    }

    // Drop every Act I virtual camera to priority 0 so none stays live at its old
    // spot over spline 1 (the "camera hanging in the textures at the map edge").
    private void LowerActICams()
    {
        if (actIRig == null) return;
        foreach (var cam in actIRig.GetComponentsInChildren<CinemachineCamera>(true))
        {
            var pr = cam.Priority; pr.Value = -100; cam.Priority = pr;
        }
    }
}
