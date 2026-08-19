using UnityEngine;
using TMPro;

// Global subtitle settings + a helper that lets any TMP text register
// itself as "a subtitle" so the player-chosen size + colour applies.
//
// Persistent keys:
//   Settings_Subtitles      0/1
//   Settings_SubtitleSize   0=Small, 1=Medium, 2=Large
public static class SubtitleSettings
{
    public enum Size { Small = 0, Medium = 1, Large = 2 }

    public static event System.Action OnChanged;

    public static bool Enabled
    {
        get => PlayerPrefs.GetInt("Settings_Subtitles", 1) == 1;
        set
        {
            PlayerPrefs.SetInt("Settings_Subtitles", value ? 1 : 0);
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }
    }

    public static Size CurrentSize
    {
        get => (Size)Mathf.Clamp(PlayerPrefs.GetInt("Settings_SubtitleSize", 1), 0, 2);
        set
        {
            PlayerPrefs.SetInt("Settings_SubtitleSize", (int)value);
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }
    }

    public static float ScaleMultiplier
    {
        get
        {
            switch (CurrentSize)
            {
                case Size.Small:  return 0.85f;
                case Size.Large:  return 1.35f;
                default:          return 1f;
            }
        }
    }

    // Translucent box behind subtitle text for legibility over bright
    // scenery. Default ON.
    public static bool BackgroundEnabled
    {
        get => PlayerPrefs.GetInt("Settings_SubtitleBg", 1) == 1;
        set
        {
            PlayerPrefs.SetInt("Settings_SubtitleBg", value ? 1 : 0);
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }
    }

    // Attach a SubtitleBinding to a TMP so the player's size / background
    // choices actually apply. The audit found SubtitleBinding was
    // referenced nowhere — so size + background were phantom. Subtitle
    // displays call this on their label in Start to wire it up.
    public static void Register(TMP_Text label)
    {
        if (label == null) return;
        if (label.GetComponent<SubtitleBinding>() == null)
            label.gameObject.AddComponent<SubtitleBinding>();
    }
}

// Drop this on any TMP_Text used as a subtitle (or let
// SubtitleSettings.Register add it). Applies the player's chosen size
// and toggles a translucent legibility box behind the text.
[RequireComponent(typeof(TextMeshProUGUI))]
public class SubtitleBinding : MonoBehaviour
{
    private TextMeshProUGUI text;
    private float baseFontSize;
    private UnityEngine.UI.Image bgBox;

    private void Awake()
    {
        text = GetComponent<TextMeshProUGUI>();
        baseFontSize = text.fontSize;
    }

    private void OnEnable()
    {
        SubtitleSettings.OnChanged += Apply;
        Apply();
    }

    private void OnDisable()
    {
        SubtitleSettings.OnChanged -= Apply;
    }

    // Rebuild the background each frame's text-change is overkill; the
    // box stretches to the text's rect and only its visibility flips
    // with the setting + whether there's text to show.
    private void LateUpdate()
    {
        if (bgBox == null) return;
        bool show = SubtitleSettings.BackgroundEnabled
                 && SubtitleSettings.Enabled
                 && !string.IsNullOrEmpty(text.text);
        if (bgBox.enabled != show) bgBox.enabled = show;
    }

    private void Apply()
    {
        if (text == null) return;
        text.fontSize = baseFontSize * SubtitleSettings.ScaleMultiplier;
        // Force bright white so subtitles stay readable over the game's dark
        // scenery. Rich-text <color=..> tags in a line still override this
        // locally (e.g. the blue [TIP] hint). Alpha is preserved so fades work.
        text.color = new Color(1f, 1f, 1f, text.color.a <= 0f ? 1f : text.color.a);
        // Rich text MUST be on — the typewriters reveal in place with an
        // <alpha=#00> tag; if a label has richText off, that tag is ignored and
        // the whole line shows at once (no typing) and never hides.
        text.richText = true;
        text.gameObject.SetActive(SubtitleSettings.Enabled);
        EnsureBgBox();
    }

    private void EnsureBgBox()
    {
        if (bgBox != null) return;
        // A dark translucent panel parented UNDER the text (first sibling
        // so it draws behind) that fills the text's rect with padding.
        var go = new GameObject("SubtitleBg", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        go.transform.SetParent(text.transform, false);
        go.transform.SetAsFirstSibling();
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        // Negative offsets → the box extends a little past the text edges
        // as padding.
        rt.offsetMin = new Vector2(-24f, -12f);
        rt.offsetMax = new Vector2(24f, 12f);
        bgBox = go.GetComponent<UnityEngine.UI.Image>();
        bgBox.color = new Color(0f, 0f, 0f, 0.5f);
        bgBox.raycastTarget = false;
        bgBox.enabled = false; // LateUpdate flips it based on text + setting
    }
}
