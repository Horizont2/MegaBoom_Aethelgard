using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Cinemachine;

// One-click wiring for Act I (the road ride). Run it AFTER you've:
//   * drawn a Spline along the road (any name containing "road", e.g. Spline_road),
//   * built the camera rig (Tools ▸ Lore Trailer ▸ Build Camera Rig).
//
//   Tools ▸ Lore Trailer ▸ Setup Act I Road Ride
//
// It finds the horse and the road spline, puts a configured TrailerHorseRide +
// HorseAudioController on the horse (auto-fit timing, auto-play, no Timeline
// keys — so no mid-timeline teleport), and points the Act I tracking cameras
// (CM_02 / CM_03 / CM_04) at the horse. It does NOT position cameras or draw
// splines (that's visual work), and it prints how to flip the facing if the
// horse runs backwards.
public static class ActIRoadRideSetup
{
    [MenuItem("Tools/Lore Trailer/Setup Act I Road Ride")]
    public static void Setup()
    {
        Transform horse = FindHorse();
        if (horse == null)
        {
            EditorUtility.DisplayDialog("Act I Road Ride",
                "No horse found. Expected an object named like 'Evacuation_Horse' (or containing 'horse') in the open scene.", "OK");
            return;
        }

        SplineContainer road = FindRoadSpline();
        if (road == null)
        {
            EditorUtility.DisplayDialog("Act I Road Ride",
                "No road Spline found. Draw one along the road (GameObject ▸ Spline) — name it so it contains 'road' (e.g. Spline_road) — then run this again.", "OK");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(horse.gameObject, "Setup Act I Road Ride");

        // Horse ride: auto-fit over Act I's length, auto-play, NOT timeline-driven
        // (a progress-01 Animation track was what teleported it mid-timeline).
        var ride = horse.GetComponent<TrailerHorseRide>();
        if (ride == null) ride = horse.gameObject.AddComponent<TrailerHorseRide>();
        ride.path = road;
        ride.autoFitSeconds = 18f;      // ~ Act I duration
        ride.playOnStart = true;
        ride.driveFromTimeline = false;
        ride.faceAlongPath = true;
        ride.loop = false;

        if (horse.GetComponent<HorseAudioController>() == null)
            horse.gameObject.AddComponent<HorseAudioController>();

        // Point the Act I tracking cameras at the horse (CM_01 is the empty road,
        // CM_02/03/04 follow the rider).
        int retargeted = 0;
        foreach (var cam in Object.FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (cam == null) continue;
            string n = cam.name;
            if (n.Contains("CM_02") || n.Contains("CM_03") || n.Contains("CM_04"))
            {
                Undo.RecordObject(cam, "Retarget Act I camera");
                var t = cam.Target;
                t.TrackingTarget = horse;
                cam.Target = t;
                EditorUtility.SetDirty(cam);
                retargeted++;
            }
        }

        EditorUtility.SetDirty(horse.gameObject);

        EditorUtility.DisplayDialog("Act I Road Ride",
            $"Wired up:\n" +
            $"  • Horse: '{horse.name}' rides '{road.name}' over {ride.autoFitSeconds:0}s (auto-play).\n" +
            $"  • HorseAudioController present.\n" +
            $"  • Retargeted {retargeted} camera(s) (CM_02/03/04) to the horse.\n\n" +
            "IF THE HORSE RUNS WRONG:\n" +
            "  • from the wrong end → tick 'Reverse' on TrailerHorseRide.\n" +
            "  • backwards / spine-first → set 'Model Yaw Offset' to 180.\n\n" +
            "ALSO: delete any Animation track that keys the horse's Progress 01 on the\n" +
            "Timeline — the ride auto-plays now, and those keys cause the teleport.\n\n" +
            "Press Play (or scrub the camera Timeline while pressing Play) to preview.", "OK");
    }

    private static Transform FindHorse()
    {
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        // Prefer an exact-ish evacuation horse, else anything with 'horse'.
        Transform exact = all.FirstOrDefault(t => t.name.Replace("_", "").ToLowerInvariant().Contains("evacuationhorse"));
        if (exact != null) return exact;
        return all.FirstOrDefault(t => t.name.ToLowerInvariant().Contains("horse"));
    }

    private static SplineContainer FindRoadSpline()
    {
        var splines = Object.FindObjectsByType<SplineContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (splines == null || splines.Length == 0) return null;
        // Prefer a spline whose name mentions the road; avoid obvious camera rails.
        var road = splines.FirstOrDefault(s => s.name.ToLowerInvariant().Contains("road"));
        if (road != null) return road;
        var nonCam = splines.FirstOrDefault(s =>
        {
            string n = s.name.ToLowerInvariant();
            return !n.Contains("paralel") && !n.Contains("parallel") && !n.Contains("cam");
        });
        return nonCam != null ? nonCam : splines[0];
    }
}
