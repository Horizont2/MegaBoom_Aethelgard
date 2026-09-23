using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Tools > Store Screenshots — one entry per shot on the Steam page.
//
// Each one opens the scene the shot belongs to, forces the region it needs,
// enters Play Mode and hands over to ScreenshotDirector, which stages the frame
// and writes the PNG. The menu itself only decides WHICH shot; everything about
// how it looks lives in the director, where it can be read and changed.
//
// The entries say AUTO or STAGED, because the difference matters before you
// press rather than after. AUTO writes a file and is done with you. STAGED sets
// the world, the hour, the sky and the spawns, then gives the game back so you
// can pick the moment — which is the one part of a screenshot a script has no
// business guessing at.
public static class StoreScreenshots
{
    [MenuItem("Tools/Store Screenshots/1 — The castle in the fog  (AUTO)", priority = 0)]
    private static void Castle() { Launch(ScreenshotDirector.CastleShot, "the castle, region 24, storm at night"); }

    [MenuItem("Tools/Store Screenshots/2 — Tundra vista  (AUTO)", priority = 1)]
    private static void Tundra() { Launch(ScreenshotDirector.TundraShot, "the Desolate Tundra, morning snow"); }

    [MenuItem("Tools/Store Screenshots/3 — A reliquary on its pad  (AUTO)", priority = 2)]
    private static void Reliquary() { Launch(ScreenshotDirector.ReliquaryShot, "a reliquary at first light"); }

    [MenuItem("Tools/Store Screenshots/4 — Totem and its anchors  (AUTO)", priority = 3)]
    private static void Totem() { Launch(ScreenshotDirector.TotemShot, "a shielded region totem, HUD on"); }

    [MenuItem("Tools/Store Screenshots/5 — The camp at evening  (AUTO)", priority = 4)]
    private static void Camp() { Launch(ScreenshotDirector.CampShot, "the camp and everything built in it"); }

    [MenuItem("Tools/Store Screenshots/6 — Horde fight  (STAGED — you press F10)", priority = 5)]
    private static void Horde() { Launch(ScreenshotDirector.HordeShot, "Howling Valley, midday, spawning turned up"); }

    private static void Launch(string shot, string what)
    {
        string scenePath = "Assets/Scenes/" + ScreenshotDirector.SceneFor(shot) + ".unity";

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Store Screenshots",
                "Stop Play Mode first — the scene cannot be opened while the game is running.", "OK");
            return;
        }

        if (!System.IO.File.Exists(scenePath))
        {
            EditorUtility.DisplayDialog("Store Screenshots", "Could not find " + scenePath + ".", "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (SceneManager.GetActiveScene().path != scenePath)
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        // Read back in AfterSceneLoad and consumed there, so a later Play does
        // not stage a screenshot nobody asked for.
        SessionState.SetString(ScreenshotDirector.SessionKey, shot);

        // The trailer recorder must not start filming a screenshot run. It arms
        // itself from its own key, which nothing here sets — but it also reacts
        // to the shot opening, so it is told plainly to stand down.
        SessionState.EraseString("Trailer.AutoRecord.Shot");

        Debug.Log("[Store Screenshots] Staging " + what + ". The world is generated first, so give it a moment.");

        EditorApplication.isPlaying = true;
    }
}
