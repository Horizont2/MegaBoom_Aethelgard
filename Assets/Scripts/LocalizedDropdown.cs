using System.Collections.Generic;
using UnityEngine;
using TMPro;

// Localises the OPTION labels + caption of every TMP_Dropdown. AutoLocalize
// deliberately skips dropdowns (it would fight the dropdown's own caption
// rendering), so their option lists — quality Low/Medium/High, window mode,
// anti-aliasing, subtitles On/Off, etc. — were never translated. This component
// auto-attaches to every dropdown in the scene, captures the original English
// option strings as keys, and re-pulls translations on every language change.
//
// Safe by construction: a label with no registered key falls through to its
// original text, so dynamic dropdowns (resolutions "1920 x 1080", monitor
// names) and the language selector (English / Українська / …) are left as-is.
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Dropdown))]
public class LocalizedDropdown : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        AttachAll();
    }

    private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) => AttachAll();

    private static void AttachAll()
    {
        var dropdowns = Object.FindObjectsByType<TMP_Dropdown>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var d in dropdowns)
        {
            if (d == null) continue;
            if (d.gameObject.tag == "DontLocalize") continue;
            if (d.GetComponent<LocalizedDropdown>() != null) continue;
            d.gameObject.AddComponent<LocalizedDropdown>();
        }
    }

    private TMP_Dropdown dropdown;
    private string[] originalKeys;   // captured original option labels
    private int lastSeenCount = -1;

    private void Awake()
    {
        dropdown = GetComponent<TMP_Dropdown>();
    }

    // Several dropdowns (resolution, monitor, window mode, fps cap) are filled
    // AFTER Awake by SettingsUI. Watch the option count and re-capture + re-apply
    // when it changes so those get localised too, without needing an event hook.
    private void Update()
    {
        if (dropdown == null) return;
        if (dropdown.options.Count != lastSeenCount)
        {
            originalKeys = null; // force re-capture of the new English labels
            Apply();
        }
    }

    private void OnEnable()
    {
        CaptureIfNeeded();
        LocalizationManager.OnLanguageChanged += Apply;
        Apply();
    }

    private void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= Apply;
    }

    private void CaptureIfNeeded()
    {
        if (dropdown == null) dropdown = GetComponent<TMP_Dropdown>();
        if (dropdown == null) return;
        // (Re)capture when the option count changed — some dropdowns are filled
        // programmatically after Awake (resolutions, monitors).
        if (originalKeys != null && originalKeys.Length == dropdown.options.Count) return;
        originalKeys = new string[dropdown.options.Count];
        for (int i = 0; i < dropdown.options.Count; i++)
            originalKeys[i] = dropdown.options[i].text;
        lastSeenCount = dropdown.options.Count;
    }

    private void Apply()
    {
        if (dropdown == null) return;
        CaptureIfNeeded();
        if (originalKeys == null) return;

        bool changed = false;
        int n = Mathf.Min(originalKeys.Length, dropdown.options.Count);
        for (int i = 0; i < n; i++)
        {
            string key = originalKeys[i];
            if (string.IsNullOrEmpty(key)) continue;
            string tr = LocalizationManager.Tr(key);
            if (dropdown.options[i].text != tr) { dropdown.options[i].text = tr; changed = true; }
        }

        // Refresh the visible caption to the (possibly) translated current value.
        if (changed && dropdown.captionText != null)
            dropdown.RefreshShownValue();
    }
}
