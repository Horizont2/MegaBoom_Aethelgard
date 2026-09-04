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
            foreach (var g in TrailerFind.AllByName(n))
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
        // Reuse the rig that is already in the scene even when it is DISABLED
        // (GameObject.Find skips disabled objects, which is how the scene ended up
        // with six of these), and delete any duplicates a previous run left.
        var existingRigs = TrailerFind.AllByName(RigName);
        GameObject rig = existingRigs.Count > 0 ? existingRigs[0] : null;
        for (int i = existingRigs.Count - 1; i >= 1; i--)
            Undo.DestroyObjectImmediate(existingRigs[i]);
        if (existingRigs.Count > 1)
            Debug.Log($"[Trailer] Removed {existingRigs.Count - 1} duplicate '{RigName}' object(s) left by earlier runs.");

        if (rig == null) { rig = new GameObject(RigName); Undo.RegisterCreatedObjectUndo(rig, "part2 rig"); }
        if (!rig.activeSelf) { Undo.RecordObject(rig, "enable"); rig.SetActive(true); }

        // Cinematic Part-2 coverage with cuts: fleeing (fear) -> side -> a
        // front angle for the look-back -> a tight close-up for the strike.
        // Low, grounded, dramatic angles for fear (not a floaty overhead follow).
        // Find the RIDER so the cameras frame HIM (face/fear), not the horse's belly.
        Transform riderT = null;
        foreach (var a in ride.GetComponentsInChildren<Animator>(true))
            if (a != null && a.transform != ride.transform) { riderT = a.transform; break; }
        Transform aim = riderT != null ? riderT : ride.transform;

        // Direction: low along the GROUND beside him, then a shot into his FACE
        // (lands on the look-back), then the lightning + fall.
        var camSide = MakeFollowCam(rig, "CM_Part2_LowSide", ride.transform, aim, new Vector3(3.2f, 1.0f, 0.2f), 38f);
        var camFace = MakeFollowCam(rig, "CM_Part2_Face", ride.transform, aim, new Vector3(0.9f, 1.9f, 5.8f), 34f);
        var camStrike = MakeFollowCam(rig, "CM_Part2_Strike", ride.transform, aim, new Vector3(2.6f, 1.6f, -3.0f), 34f);

        var cutter = rig.GetComponent<TrailerCameraCutter>();
        if (cutter == null) cutter = Undo.AddComponent<TrailerCameraCutter>(rig);
        Undo.RecordObject(cutter, "part2 cuts");
        cutter.cameras = new[] { camSide, camFace, camStrike };
        cutter.useProgress = true;
        cutter.ride = ride;
        cutter.cutProgress = new[] { 0f, 0.45f, 0.82f };   // face cut lands on the look-back; strike shot before the ~0.9 bolt
        EditorUtility.SetDirty(cutter);

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

    private static CinemachineCamera MakeFollowCam(GameObject rig, string name, Transform horse, Transform lookAt, Vector3 offset, float fov)
    {
        var existing = rig.GetComponentsInChildren<CinemachineCamera>(true).FirstOrDefault(c => c.name == name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name);
        if (existing == null) { Undo.RegisterCreatedObjectUndo(go, "cam"); go.transform.SetParent(rig.transform, false); }

        var cam = go.GetComponent<CinemachineCamera>() ?? go.AddComponent<CinemachineCamera>();
        cam.Lens.FieldOfView = fov;
        var tgt = cam.Target; tgt.TrackingTarget = horse; tgt.LookAtTarget = lookAt; cam.Target = tgt;
        var pr = cam.Priority; pr.Value = 0; cam.Priority = pr;   // the cutter raises the live one

        var follow = go.GetComponent<CinemachineFollow>() ?? go.AddComponent<CinemachineFollow>();
        follow.FollowOffset = offset;
        var ts = follow.TrackerSettings;
        // Tighter damping on the close-up so cuts feel crisp; smoother on the wide flee.
        ts.PositionDamping = new Vector3(0.7f, 0.7f, 0.9f);
        ts.RotationDamping = new Vector3(0.6f, 0.6f, 0.6f);
        follow.TrackerSettings = ts;

        var comp = go.GetComponent<CinemachineRotationComposer>() ?? go.AddComponent<CinemachineRotationComposer>();
        comp.Damping = new Vector2(0.45f, 0.45f);
        return cam;
    }

    private static SplineContainer FindSpline(string needle)
    {
        return Object.FindObjectsByType<SplineContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(s => s.name.ToLowerInvariant().Contains(needle));
    }
}
