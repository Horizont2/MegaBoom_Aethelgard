using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// A lightweight, self-building, NON-blocking tutorial side panel.
//
// Unlike the modal TutorialPanelUI (which pauses the game), this slides a
// styled card in from the left the moment a tip becomes relevant — BEFORE the
// action — and slides it out the instant the player performs the action
// (clearWhen predicate). Nothing to wire in a scene: it lazily builds its own
// DontDestroyOnLoad canvas + card in code.
public class SmartTutorialHint : MonoBehaviour
{
    private static SmartTutorialHint _inst;
    public static SmartTutorialHint Instance
    {
        get { if (_inst == null) Build(); return _inst; }
    }

    private CanvasGroup _group;
    private RectTransform _card;
    private Image _accent;
    private TextMeshProUGUI _title;
    private TextMeshProUGUI _body;
    private Coroutine _active;
    private float _cardWidth = 470f;
    private float _shownX = 46f;

    private static void Build()
    {
        var go = new GameObject("[SmartTutorialHint]");
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<SmartTutorialHint>();
        _inst.Construct();
    }

    private void Construct()
    {
        var canvasGO = new GameObject("HintCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _group = canvasGO.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        // The card, anchored to the middle-left, pivot on its left edge.
        var cardGO = new GameObject("Card", typeof(RectTransform), typeof(Image));
        cardGO.transform.SetParent(canvasGO.transform, false);
        _card = (RectTransform)cardGO.transform;
        _card.anchorMin = _card.anchorMax = new Vector2(0f, 0.5f);
        _card.pivot = new Vector2(0f, 0.5f);
        _card.sizeDelta = new Vector2(_cardWidth, 150f);
        _card.anchoredPosition = new Vector2(-_cardWidth - 30f, 0f); // start off-screen
        var bg = cardGO.GetComponent<Image>();
        bg.color = new Color(0.06f, 0.07f, 0.09f, 0.86f);
        bg.raycastTarget = false;

        // Warm accent stripe down the left edge.
        var accentGO = new GameObject("Accent", typeof(RectTransform), typeof(Image));
        accentGO.transform.SetParent(_card, false);
        var art = (RectTransform)accentGO.transform;
        art.anchorMin = new Vector2(0f, 0f); art.anchorMax = new Vector2(0f, 1f);
        art.pivot = new Vector2(0f, 0.5f);
        art.sizeDelta = new Vector2(7f, 0f); art.anchoredPosition = Vector2.zero;
        _accent = accentGO.GetComponent<Image>();
        _accent.color = new Color(1f, 0.84f, 0.4f, 1f);
        _accent.raycastTarget = false;

        TMP_FontAsset font = TMP_Settings.defaultFontAsset;

        var titleGO = new GameObject("Title", typeof(RectTransform));
        titleGO.transform.SetParent(_card, false);
        var trt = (RectTransform)titleGO.transform;
        trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f); trt.pivot = new Vector2(0f, 1f);
        trt.offsetMin = new Vector2(28f, -46f); trt.offsetMax = new Vector2(-18f, -14f);
        _title = titleGO.AddComponent<TextMeshProUGUI>();
        if (font != null) _title.font = font;
        _title.fontSize = 26f;
        _title.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
        _title.characterSpacing = 6f;
        _title.color = new Color(1f, 0.84f, 0.4f, 1f);
        _title.text = "TIP";

        var bodyGO = new GameObject("Body", typeof(RectTransform));
        bodyGO.transform.SetParent(_card, false);
        var brt = (RectTransform)bodyGO.transform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.pivot = new Vector2(0f, 0.5f);
        brt.offsetMin = new Vector2(28f, 16f); brt.offsetMax = new Vector2(-18f, -50f);
        _body = bodyGO.AddComponent<TextMeshProUGUI>();
        if (font != null) _body.font = font;
        _body.fontSize = 25f;
        _body.enableWordWrapping = true;
        _body.enableAutoSizing = true;
        _body.fontSizeMin = 18f; _body.fontSizeMax = 27f;
        _body.color = new Color(0.94f, 0.95f, 0.97f, 1f);
        _body.text = "";

        canvasGO.SetActive(true);
    }

    // Public entry point. title may be null ("TIP" default). clearWhen ends the
    // hint early once the action is done; minShow keeps it readable; maxShow caps it.
    public void ShowHint(string title, string body, System.Func<bool> clearWhen, float minShow = 2.5f, float maxShow = 14f)
    {
        if (_active != null) StopCoroutine(_active);
        _active = StartCoroutine(Run(title, body, clearWhen, minShow, maxShow));
    }

    private IEnumerator Run(string title, string body, System.Func<bool> clearWhen, float minShow, float maxShow)
    {
        _title.text = string.IsNullOrEmpty(title) ? LocalizationManager.Tr("TIP") : LocalizationManager.Tr(title);
        // Localise, drop a redundant leading "[TIP]" (the card has its own title),
        // and swap [E]/SHIFT/etc. for gamepad glyphs when a controller is used.
        string b = LocalizationManager.Tr(body);
        if (b.StartsWith("[TIP] ")) b = b.Substring(6);
        else if (b.StartsWith("[TIP]")) b = b.Substring(5);
        _body.text = GamepadGlyphs.Apply(b.TrimStart());
        _group.alpha = 0f;

        // Slide + fade in.
        float hiddenX = -_cardWidth - 30f;
        yield return Animate(hiddenX, _shownX, 0f, 1f, 0.33f);

        float t = 0f;
        while (t < maxShow)
        {
            t += Time.unscaledDeltaTime;
            // gentle accent pulse
            if (_accent != null)
            {
                Color c = _accent.color;
                c.a = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 3f);
                _accent.color = c;
            }
            if (t >= minShow && clearWhen != null && clearWhen()) break;
            yield return null;
        }

        // Slide + fade out.
        yield return Animate(_shownX, hiddenX, 1f, 0f, 0.28f);
        _active = null;
    }

    private IEnumerator Animate(float fromX, float toX, float fromA, float toA, float dur)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            float ease = 1f - Mathf.Pow(1f - k, 3f); // ease-out
            _card.anchoredPosition = new Vector2(Mathf.Lerp(fromX, toX, ease), 0f);
            _group.alpha = Mathf.Lerp(fromA, toA, ease);
            yield return null;
        }
        _card.anchoredPosition = new Vector2(toX, 0f);
        _group.alpha = toA;
    }
}
