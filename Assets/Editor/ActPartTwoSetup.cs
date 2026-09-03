using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Splines;
using Unity.Cinemachine;

// Part 2 (after the crane time-lapse) — the winter ride on the new terrain along
// spline_p3, ending in the lightning strike. Recorded as its own take (Act I is
// left intact on its own rig); join in DaVinci.
//
//   Tools ▸ Lore Trailer ▸ Setup Part 2 (spline_p3, winter + strike)
//
// Builds: the ride on spline_p3, a KCD2 chase camera, a winter grade, and the
// lightning-strike beat (flash + thunder + neigh) near the end.
// The rider LOOK-BACK, the horse REAR-UP, the player FALL and the battle need
// real animations (see the animation list) — those beats are stubbed.
public static class ActPartTwoSetup
{
    private const string RigName = "LoreTrailer_Part2_Rig";
    private static readonly Vector3 ChaseOffset = new Vector3(2.2f, 3.6f, -7.5f);

    [MenuItem("Tools/Lore Trailer/Setup Part 2 (spline_p3, winter + strike)")]
    public static void Setup()
    {
        var ride = Object.FindObjectsByType<TrailerHorseRide>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (ride == null)
        {
            EditorUtility.DisplayDialog("Part 2", "No TrailerHorseRide found — run 'Setup Act I Road Ride' once so the horse has the ride component.", "OK");
            return;
        }

        var road = FindSpline("p3") ?? FindSpline("part2") ?? FindSpline("actii");
        if (road == null)
        {
            EditorUtility.DisplayDialog("Part 2", "No 'spline_p3' found. Draw the Part-2 route spline (name containing 'p3') and run again.", "OK");
            return;
        }

        Undo.SetCurrentGroupName("Setup Part 2");

        // Separate take: park Act I / Act II rigs.
        foreach (var n in new[] { "LoreTrailer_Rig", "LoreTrailer_ActII_Rig" })
        {
            var g = GameObject.Find(n);
            if (g != null && g.activeSelf) { Undo.RecordObject(g, "park rig"); g.SetActive(false); }
        }

        // Ride the new route.
        Undo.RecordObject(ride, "config part2 ride");
        ride.path = road;
        ride.autoFitSeconds = 0f;
        ride.speed = 9f;
        ride.playOnStart = true;
        ride.driveFromTimeline = false;
        ride.faceAlongPath = true;
        ride.loop = false;
        ride.groundSnapOverrun = true;
        EditorUtility.SetDirty(ride);

        // Part-2 rig with a chase cam + the strike event.
        var rig = GameObject.Find(RigName);
        if (rig == null) { rig = new GameObject(RigName); Undo.RegisterCreatedObjectUndo(rig, "part2 rig"); }
        if (!rig.activeSelf) { Undo.RecordObject(rig, "enable"); rig.SetActive(true); }

        MakeChaseCam(rig, ride.transform);

        var evt = rig.GetComponent<TrailerRideEvent>();
        if (evt == null) evt = Undo.AddComponent<TrailerRideEvent>(rig);
        Undo.RecordObject(evt, "config strike");
        evt.ride = ride;
        evt.strikeProgress = 0.9f;
        EditorUtility.SetDirty(evt);

        // Winter grade for the whole part.
        TrailerCinematicPostFX.Apply(TrailerCinematicPostFX.Preset.Winter, false);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        EditorUtility.DisplayDialog("Part 2",
            $"Part 2 wired on '{road.name}':\n" +
            "  • Horse rides the new route; KCD2 chase camera follows.\n" +
            "  • Winter grade applied.\n" +
            "  • Lightning strike (flash + thunder + neigh) near the end (~90% of the route).\n\n" +
            "Run 'Dress Roadside' to line spline_p3 with torches + undead (it uses the horse's current route).\n" +
            "PRESS PLAY to preview. To go back to Act I, run 'Setup Act I Road Ride'.\n\n" +
            "⚠ STILL NEEDS ANIMATIONS (see the list): rider look-back, horse rear-up, rider fall, and the battle.", "OK");
    }

    private static void MakeChaseCam(GameObject rig, Transform horse)
    {
        var existing = rig.GetComponentsInChildren<CinemachineCamera>(true).FirstOrDefault(c => c.name == "CM_Part2_Chase");
        GameObject go = existing != null ? existing.gameObject : new GameObject("CM_Part2_Chase");
        if (existing == null) { Undo.RegisterCreatedObjectUndo(go, "chase"); go.transform.SetParent(rig.transform, false); }

        var cam = go.GetComponent<CinemachineCamera>() ?? go.AddComponent<CinemachineCamera>();
        cam.Lens.FieldOfView = 44f;
        var tgt = cam.Target; tgt.TrackingTarget = horse; cam.Target = tgt;
        var pr = cam.Priority; pr.Value = 100; cam.Priority = pr;

        var follow = go.GetComponent<CinemachineFollow>() ?? go.AddComponent<CinemachineFollow>();
        follow.FollowOffset = ChaseOffset;
        var ts = follow.TrackerSettings;
        ts.PositionDamping = new Vector3(0.9f, 0.9f, 1.2f);
        ts.RotationDamping = new Vector3(0.7f, 0.7f, 0.7f);
        follow.TrackerSettings = ts;

        var comp = go.GetComponent<CinemachineRotationComposer>() ?? go.AddComponent<CinemachineRotationComposer>();
        comp.Damping = new Vector2(0.55f, 0.55f);
    }

    private static SplineContainer FindSpline(string needle)
    {
        return Object.FindObjectsByType<SplineContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(s => s.name.ToLowerInvariant().Contains(needle));
    }
}
