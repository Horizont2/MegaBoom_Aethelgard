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
// What was actually missing was the opposite thing: a way to WATCH one shot.
// Between them the two trailer scenes hold four, and every one of them opens
// itself in Start, so pressing Play ran whichever ones shared a scene at the
// same time and none of them could be judged. That is what these four do.
public static class TrailerShots
{
    // One entry per shot, in the order they are cut. Two scenes hold four shots
    // between them and every one of those shots opens itself in Start, so the
    // menu is also what decides which of them is allowed to.
    [MenuItem("Tools/Lore Trailer/Play shot 1 — the statue breaks open", priority = 0)]
    private static void PlayStatue() { Launch(TrailerShotSolo.StatueShot, "the statue breaking open"); }

    [MenuItem("Tools/Lore Trailer/Play shot 2 — the ride through the forest", priority = 1)]
    private static void PlayRide() { Launch(TrailerShotSolo.RideShot, "the ride through the forest"); }

    [MenuItem("Tools/Lore Trailer/Play shot 3 — the legion marches (alone)", priority = 2)]
    private static void PlayMarch() { Launch(TrailerShotSolo.MarchShot, "the legion marching"); }

    // The clash is the beat AFTER the march and waits on it, so this plays both.
    [MenuItem("Tools/Lore Trailer/Play shot 4 — the march, then the armies meet", priority = 3)]
    private static void PlayClash() { Launch(TrailerShotSolo.ClashShot, "the march and the clash that follows it"); }

    private static void Launch(string shot, string what)
    {
        // Asked of TrailerShotSolo rather than written here twice. The runtime
        // side refuses to run a shot in a scene it does not belong to, and the
        // two lists disagreeing is how a shot ends up half-applied to the wrong
        // scene — switching off a light that happens to share a name, handing a
        // camera solo a camera that is not there.
        string scenePath = "Assets/Scenes/" + TrailerShotSolo.SceneFor(shot) + ".unity";

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Lore Trailer",
                "Stop Play Mode first — the scene cannot be opened while the game is running.\n\n" +
                "Then pick the shot again.", "OK");
            return;
        }

        if (!System.IO.File.Exists(scenePath))
        {
            EditorUtility.DisplayDialog("Lore Trailer", "Could not find " + scenePath + ".", "OK");
            return;
        }

        // Never silently throw away someone's work in another scene.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (SceneManager.GetActiveScene().path != scenePath)
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

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
