using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Credits scroll — a single scrolling TMP block over a CanvasGroup
// panel. Auto-scrolls at a steady speed, closes on Escape / click.
// Content is a compile-time constant so the game can never ship
// without credits (Steam requires attributions for licensed assets).
[DisallowMultipleComponent]
public class CreditsUI : MonoBehaviour
{
    [Header("UI (all optional — auto-wired if left null)")]
    public CanvasGroup panel;
    public RectTransform scrollContent;
    public TextMeshProUGUI creditsText;
    public Button closeButton;

    [Header("Behaviour")]
    [Tooltip("Vertical scroll speed in pixels/second.")]
    public float scrollSpeed = 45f;
    [Tooltip("Fade in / out duration in seconds.")]
    public float fadeDuration = 0.4f;
    [Tooltip("Pause between reaching the bottom and looping / closing.")]
    public float endHoldSeconds = 3f;

    private Coroutine scrollRoutine;
    private bool isOpen;

    private void Awake()
    {
        if (panel == null) panel = GetComponent<CanvasGroup>();
        if (panel != null) { panel.alpha = 0f; panel.blocksRaycasts = false; panel.interactable = false; }
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        gameObject.SetActive(false);
    }

    public void Open()
    {
        gameObject.SetActive(true);
        isOpen = true;

        // Populate the credits body every open — cheap enough (one
        // TMP re-layout) and lets a language switch reflect immediately.
        if (creditsText != null) creditsText.text = BuildCredits();
        if (scrollContent != null) scrollContent.anchoredPosition = Vector2.zero;

        if (scrollRoutine != null) StopCoroutine(scrollRoutine);
        scrollRoutine = StartCoroutine(RunRoutine());
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        if (scrollRoutine != null) StopCoroutine(scrollRoutine);
        scrollRoutine = StartCoroutine(FadeAndDeactivate());
    }

    private void Update()
    {
        if (!isOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Space)) Close();
    }

    private IEnumerator RunRoutine()
    {
        // Fade in.
        if (panel != null)
        {
            panel.blocksRaycasts = true;
            panel.interactable = true;
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                panel.alpha = Mathf.Clamp01(t / fadeDuration);
                yield return null;
            }
            panel.alpha = 1f;
        }

        // Scroll upward until the whole block passes.
        if (scrollContent != null && creditsText != null)
        {
            creditsText.ForceMeshUpdate();
            float distance = creditsText.preferredHeight;
            float travelled = 0f;
            while (travelled < distance)
            {
                float step = scrollSpeed * Time.unscaledDeltaTime;
                travelled += step;
                scrollContent.anchoredPosition += new Vector2(0f, step);
                yield return null;
            }
        }

        yield return new WaitForSecondsRealtime(endHoldSeconds);
        Close();
    }

    private IEnumerator FadeAndDeactivate()
    {
        if (panel != null)
        {
            float startAlpha = panel.alpha;
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                panel.alpha = Mathf.Lerp(startAlpha, 0f, t / fadeDuration);
                yield return null;
            }
            panel.alpha = 0f;
            panel.blocksRaycasts = false;
            panel.interactable = false;
        }
        gameObject.SetActive(false);
    }

    // Kept compile-time so a stray designer edit doesn't ship the game
    // with missing legally-required attributions. All strings pass
    // through Tr so translators can localise section headers.
    private static string BuildCredits()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(2048);
        void H(string k) => sb.Append("\n\n<size=48><b>").Append(LocalizationManager.Tr(k)).Append("</b></size>\n");
        // Wrap body lines in Tr too — brand names / product identifiers
        // (Unity 6, BITGEM, Cinemachine, etc.) fall through unchanged
        // since they aren't registered, but prose lines get localised.
        void L(string line) => sb.Append("<size=28>").Append(LocalizationManager.Tr(line)).Append("</size>\n");

        H("CREDITS_HEADER_STUDIO");
        L("Horizont Studio");
        L("Hollow Siege / Aethelgard");

        H("CREDITS_HEADER_DESIGN");
        L("Game Design, Programming, Level Design");
        L("");

        H("CREDITS_HEADER_ART");
        L("3D Models & Environment");
        L("");

        H("CREDITS_HEADER_ENGINE");
        L("Unity 6");
        L("Universal Render Pipeline");
        L("Cinemachine");
        L("Timeline");

        H("CREDITS_HEADER_AUDIO");
        L("FMOD Studio by Firelight Technologies");
        L("");

        H("CREDITS_HEADER_ASSETS");
        L("Dreamscape by Polyart Studio");
        L("BITGEM assets");
        L("Vegetation MultiColor");
        L("SG_FlowingFog");
        L("");

        H("CREDITS_HEADER_LOCALISATION");
        L("English · Ukrainian");
        L("");

        H("CREDITS_HEADER_THANKS");
        L(LocalizationManager.Tr("CREDITS_THANKS_LINE"));
        L("");

        H("CREDITS_HEADER_COPYRIGHT");
        L("© 2026 Horizont Studio. All rights reserved.");
        L("");
        L(LocalizationManager.Tr("CREDITS_END_TAGLINE"));

        return sb.ToString();
    }
}
