using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The word that tells the player they did the hard thing.
//
// ==== WHY A WORD AND NOT JUST AN EFFECT ====
//
// The parry already flashes, rings, freezes time and kicks the camera, and it
// still was not legible — because all of that happens in the world, at the
// point of contact, in the middle of a fight where the player is watching three
// enemies and a stamina bar. World-space feedback tells you something HAPPENED.
// It does not tell you WHICH of the two things happened, and "was that a parry
// or just a block?" is precisely the question the player needs answered to
// learn the timing at all.
//
// A word in the middle of the screen answers it in one frame, every time, from
// anywhere. It is the crudest possible solution and it is the correct one — the
// entire fighting-game genre settled on exactly this for exactly this reason.
//
// ==== WHY IT IS NOT A SUBTLE ONE ====
//
// It punches in oversized, snaps back, holds for a third of a second and is
// gone. Short enough never to be in the way during a fight, loud enough to be
// caught in peripheral vision while looking at something else. A tasteful fade
// would be missed, which would make it decoration rather than information.
[DisallowMultipleComponent]
public class ParryBanner : MonoBehaviour
{
    public static ParryBanner Instance { get; private set; }

    private CanvasGroup _group;
    private TextMeshProUGUI _label;
    private RectTransform _rt;
    private Coroutine _playing;

    public static void Show(string text, Color colour)
    {
        if (Instance == null)
        {
            var go = new GameObject("[ParryBanner]");
            DontDestroyOnLoad(go);
            go.AddComponent<ParryBanner>();
        }
        if (Instance != null) Instance.Play(text, colour);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
        _group.alpha = 0f;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the HUD, below the reward reveal — a parry during a drop reveal
        // should not punch through it.
        canvas.sortingOrder = 3900;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;

        var go = new GameObject("Label", typeof(RectTransform));
        _rt = go.GetComponent<RectTransform>();
        _rt.SetParent(transform, false);
        _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
        _rt.pivot = new Vector2(0.5f, 0.5f);
        // Above centre, not on it: the middle of the screen is where the enemy
        // that was just parried is standing, and covering them hides the stagger
        // the word is telling you to punish.
        _rt.anchoredPosition = new Vector2(0f, 150f);
        _rt.sizeDelta = new Vector2(900f, 130f);

        _label = go.AddComponent<TextMeshProUGUI>();
        _label.alignment = TextAlignmentOptions.Center;
        _label.fontSize = 86f;
        _label.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        _label.raycastTarget = false;
        _label.enableWordWrapping = false;
        // A hard outline so it stays readable over a bright flash, a dark cave
        // or a snowfield without anyone tuning it per scene.
        _label.outlineWidth = 0.25f;
        _label.outlineColor = new Color32(10, 8, 4, 255);
    }

    private void Play(string text, Color colour)
    {
        if (_playing != null) StopCoroutine(_playing);
        _playing = StartCoroutine(Routine(text, colour));
    }

    private IEnumerator Routine(string text, Color colour)
    {
        _label.text = text;
        _label.color = colour;

        // UNSCALED throughout. A parry runs a hitstop that drops timeScale to
        // 0.05 — on scaled time this would still be crawling in three seconds.
        float t = 0f;
        const float punchIn = 0.09f;
        while (t < punchIn)
        {
            t += Time.unscaledDeltaTime;
            float k = t / punchIn;
            _group.alpha = k;
            _rt.localScale = Vector3.one * Mathf.Lerp(1.9f, 1f, k * k);
            yield return null;
        }
        _rt.localScale = Vector3.one;
        _group.alpha = 1f;

        t = 0f;
        while (t < 0.32f) { t += Time.unscaledDeltaTime; yield return null; }

        t = 0f;
        const float outT = 0.22f;
        while (t < outT)
        {
            t += Time.unscaledDeltaTime;
            float k = t / outT;
            _group.alpha = 1f - k;
            // Drifts up as it goes, so it leaves rather than switching off.
            _rt.anchoredPosition = new Vector2(0f, 150f + k * 34f);
            yield return null;
        }

        _group.alpha = 0f;
        _rt.anchoredPosition = new Vector2(0f, 150f);
        _playing = null;
    }
}
