using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// Bottom-right version tag on the main menu. Auto-created at runtime so
// we don't need to touch the menu prefab — QA and bug-report screenshots
// always have the version visible.
//
// Shows: v<Application.version>  (or debug tag in editor)
public static class VersionStamp
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded += (scene, mode) =>
        {
            // Only draw in the menus — in-game we already have HUD chrome
            // that would collide with a corner label.
            if (scene.name != "Menu" && scene.name != "MainMenu") return;
            Build();
        };
        // Bootstrap fires AFTER the initial scene load — sceneLoaded
        // won't retroactively fire for the menu the player is already
        // sitting on. Explicitly build now if we're in the menu.
        var current = SceneManager.GetActiveScene();
        if (current.name == "Menu" || current.name == "MainMenu") Build();
    }

    private static void Build()
    {
        var existing = GameObject.Find("[VersionStamp]");
        if (existing != null) return;

        var go = new GameObject("[VersionStamp]");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000; // above the menu, below any modal
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var textGO = new GameObject("Label", typeof(TextMeshProUGUI));
        textGO.transform.SetParent(go.transform, false);
        var rt = textGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-20f, 14f);
        rt.sizeDelta = new Vector2(500f, 40f);

        var tmp = textGO.GetComponent<TextMeshProUGUI>();
        tmp.text = BuildStamp();
        tmp.fontSize = 18f;
        tmp.color = new Color(0.75f, 0.72f, 0.62f, 0.55f);
        tmp.alignment = TextAlignmentOptions.BottomRight;
        tmp.enableWordWrapping = false;
    }

    private static string BuildStamp()
    {
        string tag = "";
#if DEVELOPMENT_BUILD
        tag = " dev";
#elif UNITY_EDITOR
        tag = " editor";
#endif
        // Append the short buildGUID (empty in the editor, populated in
        // actual builds) so bug-report screenshots identify the exact
        // build the tester was on — same version can ship multiple times.
        string guid = Application.buildGUID;
        string suffix = string.IsNullOrEmpty(guid) ? "" : "-" + guid.Substring(0, System.Math.Min(6, guid.Length));
        return $"v{Application.version}{tag}{suffix}";
    }
}
