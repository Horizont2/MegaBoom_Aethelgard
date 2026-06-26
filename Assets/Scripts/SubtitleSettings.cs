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
}

// Drop this on any TMP_Text used as a subtitle. It records the
// design-time font size at Awake and lives-multiplies it by the
// player's chosen scale. Also toggles the GameObject when subtitles
// are disabled.
[RequireComponent(typeof(TextMeshProUGUI))]
public class SubtitleBinding : MonoBehaviour
{
    private TextMeshProUGUI text;
    private float baseFontSize;

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

    private void Apply()
    {
        if (text == null) return;
        text.fontSize = baseFontSize * SubtitleSettings.ScaleMultiplier;
        text.gameObject.SetActive(SubtitleSettings.Enabled);
    }
}
