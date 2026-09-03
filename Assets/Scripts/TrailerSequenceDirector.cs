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
    [Range(0f, 1f)] public float part1EndProgress = 0.9f;
    public float timelapseSeconds = 7f;
    public float dayNightCyclesInTimelapse = 4f;

    [Header("Crane (region reveal — does NOT follow the horse)")]
    public float craneStartHeight = 10f;
    public float craneEndHeight = 48f;
    public float craneStartPitch = 25f;
    public float craneEndPitch = 55f;

    private enum Phase { Part1, Timelapse, Part2 }
    private Phase _phase = Phase.Part1;
    private float _tlT;
    private CinemachineCamera _crane;
    private Vector3 _craneAnchor;
    private float _craneYaw, _sunYaw;

    private void Start()
    {
        AutoFind();

        // Force the PART 1 starting state (Part 2 setup leaves the horse on
        // spline_p3 and the Act I rig disabled — that's why it "started at Part 2").
        if (actIRig != null) actIRig.SetActive(true);
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

    private void AutoFind()
    {
        if (ride == null) ride = Object.FindFirstObjectByType<TrailerHorseRide>();
        if (actIRig == null) actIRig = GameObject.Find("LoreTrailer_Rig");
        if (part2Rig == null) part2Rig = GameObject.Find("LoreTrailer_Part2_Rig");
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

    private void BeginTimelapse()
    {
        _phase = Phase.Timelapse; _tlT = 0f;

        // NOTE: the ride is deliberately NOT stopped — the horse keeps galloping
        // away (overrun) so it never stands still running on the spot.
        StopActIDirector();
        LowerActICams();

        _craneAnchor = ride.transform.position;
        _craneYaw = ride.transform.eulerAngles.y;
        if (_crane != null)
        {
            var pr = _crane.Priority; pr.Value = 200; _crane.Priority = pr;
            PlaceCrane(0f);
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

        // Move horse + rider (parented) onto spline_p3, already running.
        if (part2Spline != null)
        {
            ride.path = part2Spline;
            ride.autoFitSeconds = part2RideSeconds;
            ride.progress01 = 0f;
            ride.enabled = true;
            ride.BeginRide();
        }

        LowerActICams();
        // HARD CUT: switching off the live crane makes the brain snap to the Part 2
        // camera instead of flying across the map to it.
        if (_crane != null) _crane.gameObject.SetActive(false);
        if (part2Rig != null) part2Rig.SetActive(true);
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
