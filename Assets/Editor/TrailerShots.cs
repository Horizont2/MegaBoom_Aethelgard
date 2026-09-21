using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// The whole Tools > Lore Trailer menu, which is now two entries: play shot 1,
// play shot 2.
//
// ==== WHY THE THIRTEEN SETUP TOOLS ARE GONE ====
//
// They were scaffolding. Each one BUILT a piece of the trailer into the scene —
// the camera rig, the road dressing, the weather, the seasons, the statue — and
// every one of them has already been run; what they built is saved in
// Trailer_Lvl_1.unity and has been hand-tuned since. Running any of them again
// would not have reproduced the shot, it would have added a second copy of a rig
// next to the tuned one, which is exactly what the duplicate-rig comment in
// TrailerFind is about. A builder you must never press is worse than no builder.
//
// What was actually missing was the opposite thing: a way to WATCH one shot. The
// scene holds two finished shots that both open themselves in Start, so Play ran
// both at once and neither could be judged. That is what these two do.
public static class TrailerShots
{
    // Kept in step with the guard inside TrailerShotSolo, which refuses to touch
    // any other scene.
    private const string ScenePath = "Assets/Scenes/" + TrailerShotSolo.TrailerScene + ".unity";

    [MenuItem("Tools/Lore Trailer/Play shot 1 — the statue breaks open", priority = 0)]
    private static void PlayStatue() { Launch(TrailerShotSolo.StatueShot, "the statue breaking open"); }

    [MenuItem("Tools/Lore Trailer/Play shot 2 — the ride through the forest", priority = 1)]
    private static void PlayRide() { Launch(TrailerShotSolo.RideShot, "the ride through the forest"); }

    private static void Launch(string shot, string what)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Lore Trailer",
                "Stop Play Mode first — the scene cannot be opened while the game is running.\n\n" +
                "Then pick the shot again.", "OK");
            return;
        }

        if (!System.IO.File.Exists(ScenePath))
        {
            EditorUtility.DisplayDialog("Lore Trailer", "Could not find " + ScenePath + ".", "OK");
            return;
        }

        // Never silently throw away someone's work in another scene.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Read back in AfterSceneLoad by TrailerShotSolo, before any director's
        // Start, which is the only moment at which the other shot can still be
        // stopped from taking the camera. SessionState and not EditorPrefs: this
        // should not outlive the editor session.
        SessionState.SetString(TrailerShotSolo.SessionKey, shot);

        Debug.Log("[Lore Trailer] Playing " + what + ". The other shot is switched off for this run only — " +
                  "the scene asset is not modified. Press Play again to replay the same shot; " +
                  "pick the other entry in Tools > Lore Trailer to switch.");

        EditorApplication.isPlaying = true;
    }
}
