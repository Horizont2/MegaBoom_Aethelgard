using UnityEditor;
using UnityEngine;

// Adds the master sequence director so the whole trailer plays in one Play:
//   Part 1 ride → crane-hold time-lapse (fast day/night, autumn → winter) →
//   auto-transition to Part 2 on spline_p3 (winter).
//
//   Tools ▸ Lore Trailer ▸ Setup Full Trailer Sequence
//
// Run the other setups first (Act I, Act II Seasons, Part 2, Setup Cutscene
// Animations) and draw spline_p3; this only drops in the coordinator.
public static class TrailerSequenceSetup
{
    [MenuItem("Tools/Lore Trailer/Setup Full Trailer Sequence")]
    public static void Setup()
    {
        var go = GameObject.Find("TrailerSequencer");
        if (go == null)
        {
            go = new GameObject("TrailerSequencer");
            Undo.RegisterCreatedObjectUndo(go, "Trailer sequencer");
        }
        if (go.GetComponent<TrailerSequenceDirector>() == null)
            Undo.AddComponent<TrailerSequenceDirector>(go);

        var dir = go.GetComponent<TrailerSequenceDirector>();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        EditorUtility.DisplayDialog("Full Trailer Sequence",
            "Added 'TrailerSequencer'. On Play it runs:\n" +
            "  1. Act I ride (spline 1) with the Act I cameras.\n" +
            "  2. At ~90% of the ride: camera cranes UP + holds while the sun races\n" +
            "     several days and the world turns autumn → winter.\n" +
            "  3. Then the horse + rider move to spline_p3 and gallop on through\n" +
            "     winter; the Part 2 rig (cuts, lightning, rear, fall, skeletons) plays.\n\n" +
            "Prereqs (run these first if not done):\n" +
            "  • Setup Act I Road Ride, Setup Act II Seasons, Setup Part 2, Setup Cutscene Animations\n" +
            "  • draw spline_p3 for Part 2\n\n" +
            $"Auto-found now: spline_p3 {(dir != null ? "will resolve at Play" : "-")}.\n" +
            "Tune part1EndProgress / timelapseSeconds on the TrailerSequencer.", "OK");
    }
}
