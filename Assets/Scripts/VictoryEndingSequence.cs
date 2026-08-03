using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

// Post-final-boss ending sequence. Fires when the player conquers
// region 24 (The Throne Room). Flow:
//   1. Fade to black (2 s).
//   2. Ending narration (typewriter, three beats).
//   3. Fade to full black, "You have restored Aethelgard" beat.
//   4. Auto-open the CreditsUI.
//   5. On credits end, return to main menu with victory flag set.
//
// Kicks in automatically via MapProgressionManager.OnMapStateChanged —
// no scene / prefab wiring required. Self-instantiates a screen-space
// overlay canvas so it works on any scene that leaves the last region.
public class VictoryEndingSequence : MonoBehaviour
{
    private static VictoryEndingSequence s_instance;
    private static bool s_alreadyFired;

    // Zero-config bootstrap: subscribe to map changes on scene load and
    // instantiate when needed.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Bootstrap()
    {
        MapProgressionManager.OnMapStateChanged -= CheckVictory;
        MapProgressionManager.OnMapStateChanged += CheckVictory;
    }

    private static void CheckVictory()
    {
        if (s_alreadyFired) return;

        // Once GameSave/SaveSystem is authoritative, use SaveSystem.Current.
        // For now read the legacy key (still written on conquest).
        int conquered = PlayerPrefs.GetInt("TotalConqueredRegions", 0);
        if (conquered < 24) return;

        s_alreadyFired = true;
        var go = new GameObject("[VictoryEndingSequence]");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<VictoryEndingSequence>();
        s_instance.StartCoroutine(s_instance.RunSequence());
    }

    private CanvasGroup fadeGroup;
    private TextMeshProUGUI narrationText;

    private void Awake()
    {
        BuildOverlay();
    }

    private void BuildOverlay()
    {
        var canvasGO = new GameObject("EndingCanvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = canvasGO.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // Full-screen black fade.
        var fadeGO = new GameObject("Fade", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(CanvasGroup));
        fadeGO.transform.SetParent(canvasGO.transform, false);
        var fadeRT = (RectTransform)fadeGO.transform;
        fadeRT.anchorMin = Vector2.zero;
        fadeRT.anchorMax = Vector2.one;
        fadeRT.offsetMin = Vector2.zero;
        fadeRT.offsetMax = Vector2.zero;
        fadeGO.GetComponent<UnityEngine.UI.Image>().color = Color.black;
        fadeGroup = fadeGO.GetComponent<CanvasGroup>();
        fadeGroup.alpha = 0f;
        fadeGroup.blocksRaycasts = false;

        // Centered narration TMP.
        var narrGO = new GameObject("Narration", typeof(RectTransform), typeof(TextMeshProUGUI));
        narrGO.transform.SetParent(fadeGO.transform, false);
        var narrRT = (RectTransform)narrGO.transform;
        narrRT.anchorMin = new Vector2(0.1f, 0.4f);
        narrRT.anchorMax = new Vector2(0.9f, 0.6f);
        narrRT.offsetMin = Vector2.zero;
        narrRT.offsetMax = Vector2.zero;
        narrationText = narrGO.GetComponent<TextMeshProUGUI>();
        narrationText.alignment = TextAlignmentOptions.Center;
        narrationText.fontSize = 52f;
        narrationText.color = new Color(0.95f, 0.9f, 0.75f);
        narrationText.text = "";
    }

    private IEnumerator RunSequence()
    {
        AchievementSystem.Unlock("THRONE_TAKEN");

        // Block player input for the full sequence.
        var pc = PlayerController.LocalInstance;
        if (pc != null) pc.isControlBlocked = true;

        // 1. Fade to black.
        yield return Fade(0f, 1f, 2f);

        // 2. Three narration beats.
        yield return TypeLine(LocalizationManager.Tr("ENDING_LINE_1"), 3.2f);
        yield return TypeLine(LocalizationManager.Tr("ENDING_LINE_2"), 3.2f);
        yield return TypeLine(LocalizationManager.Tr("ENDING_LINE_3"), 3.5f);

        // 3. Big finish beat.
        yield return TypeLine(LocalizationManager.Tr("ENDING_LINE_FINAL"), 4f);

        // 4. Open credits over the black background.
        narrationText.text = "";
        var creditsGO = new GameObject("EndingCredits", typeof(RectTransform), typeof(CanvasGroup));
        creditsGO.transform.SetParent(narrationText.transform.parent, false);
        var creditsRT = (RectTransform)creditsGO.transform;
        creditsRT.anchorMin = Vector2.zero; creditsRT.anchorMax = Vector2.one;
        creditsRT.offsetMin = Vector2.zero; creditsRT.offsetMax = Vector2.zero;
        var credits = creditsGO.AddComponent<CreditsUI>();
        credits.Open();

        // Wait until credits either close on their own or the player
        // presses Esc — CreditsUI deactivates itself when done.
        while (creditsGO != null && creditsGO.activeSelf) yield return null;

        // 5. Return to main menu — game is done.
        SceneLoader.LoadScene("Menu");
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            fadeGroup.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        fadeGroup.alpha = to;
    }

    private IEnumerator TypeLine(string text, float holdSeconds)
    {
        narrationText.text = text;
        narrationText.maxVisibleCharacters = 0;
        narrationText.ForceMeshUpdate();
        int total = narrationText.textInfo.characterCount;
        for (int i = 0; i <= total; i++)
        {
            narrationText.maxVisibleCharacters = i;
            yield return new WaitForSecondsRealtime(0.035f);
        }
        narrationText.maxVisibleCharacters = 99999;
        yield return new WaitForSecondsRealtime(holdSeconds);
    }
}
