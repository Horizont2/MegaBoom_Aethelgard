using UnityEngine;
using TMPro;
using UnityEngine.UI;

// Drop-in component for any text that should follow the in-game
// language setting. Attach to a TextMeshProUGUI (preferred) or legacy
// Text, set `key` to the localization key, and the component pulls
// the translation on Enable and re-pulls every time
// LocalizationManager.OnLanguageChanged fires.
//
// Usage in code:   GetComponent<LocalizedText>().SetKey("ui.start");
// Usage in editor: paste the key into the Key field.
[DisallowMultipleComponent]
public class LocalizedText : MonoBehaviour
{
    [Tooltip("Localization key. Falls back to the key string itself when no translation is registered.")]
    public string key;

    [Tooltip("Optional string-format args. Applied via LocalizationManager.Tr(key, args).")]
    public string[] formatArgs;

    private TextMeshProUGUI tmp;
    private Text legacy;

    private void Awake()
    {
        tmp = GetComponent<TextMeshProUGUI>();
        if (tmp == null) legacy = GetComponent<Text>();
    }

    private void OnEnable()
    {
        LocalizationManager.OnLanguageChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= Refresh;
    }

    public void SetKey(string newKey)
    {
        key = newKey;
        Refresh();
    }

    public void Refresh()
    {
        if (string.IsNullOrEmpty(key)) return;
        string translated = (formatArgs != null && formatArgs.Length > 0)
            ? LocalizationManager.Tr(key, formatArgs)
            : LocalizationManager.Tr(key);
        if (tmp != null) tmp.text = translated;
        else if (legacy != null) legacy.text = translated;
    }
}
