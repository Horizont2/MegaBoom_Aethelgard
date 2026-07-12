using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// Persistent codex of lore fragments. Entries are seeded in
// LocalizationManager (LORE_* keys with _TITLE and _BODY pairs).
// ScrollPickup unlocks them in-world; LoreCodexUIController reads
// from here when the player opens the codex panel.
public static class LoreCodexManager
{
    public static readonly string[] AllLoreIDs = new[]
    {
        "LORE_AETHELGARD_FALL",
        "LORE_AETHER_SHARDS",
        "LORE_BONE_TIDE",
        "LORE_TOTEMS",
        "LORE_THE_FORGE",
        "LORE_NIGHT_BUFF",
        "LORE_THE_PALE_KING",
        "LORE_STRANGER",
        "LORE_FORGE_MOTHER",
        "LORE_WATCHFIRE",
        "LORE_DEAD_RIVER",
        "LORE_BLOOD_OATH",
    };

    private const string UnlockedPrefix = "Lore_Unlocked_";

    public static event System.Action<string> OnLoreUnlocked;

    public static bool IsUnlocked(string id) => PlayerPrefs.GetInt(UnlockedPrefix + id, 0) == 1;

    public static int UnlockedCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < AllLoreIDs.Length; i++) if (IsUnlocked(AllLoreIDs[i])) n++;
            return n;
        }
    }

    public static int TotalCount => AllLoreIDs.Length;

    public static void Unlock(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (IsUnlocked(id)) return;
        PlayerPrefs.SetInt(UnlockedPrefix + id, 1);
        PlayerPrefs.Save();

        string title = LocalizationManager.Tr(id + "_TITLE");
        OnLoreUnlocked?.Invoke(id);
        RunStats.Add(RunStats.Stat.ScrollsFound, 1);
        AchievementManager.Notify(AchievementManager.Tracker.ScrollFound, 1);
        ToastManager.Show(LocalizationManager.Tr("TOAST_LORE_FOUND", title), ToastManager.ToastKind.Lore);
    }
}

// In-world collectible. Place on a scroll prefab in a region scene,
// set `loreID` to one of LoreCodexManager.AllLoreIDs in the inspector,
// and the player will unlock that lore entry on touch / press [E].
[RequireComponent(typeof(Collider))]
public class ScrollPickup : MonoBehaviour
{
    [Tooltip("LocalizationManager LORE_* id to unlock when this scroll is picked up.")]
    public string loreID = "LORE_AETHELGARD_FALL";
    [Tooltip("True = unlock on trigger enter. False = require [E] keypress while in range.")]
    public bool autoUnlockOnTouch = true;
    [Tooltip("Prompt text shown when autoUnlockOnTouch is false (only if GlobalHUD is present).")]
    public string promptText = "[E] Read scroll";

    private bool playerInRange;
    private bool consumed;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = true;
        if (autoUnlockOnTouch) Claim();
        else if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt(promptText);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = false;
        if (!autoUnlockOnTouch && GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
    }

    private void Update()
    {
        if (consumed) return;
        if (autoUnlockOnTouch) return;
        if (playerInRange && Input.GetKeyDown(KeyCode.E)) Claim();
    }

    private void Claim()
    {
        if (consumed) return;
        consumed = true;
        LoreCodexManager.Unlock(loreID);
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
        Destroy(gameObject, 0.05f);
    }
}

// Wire this onto your existing codex panel (whatever style you build
// in the editor). Required inspector references:
//
//   panelRoot        — the GameObject that holds the whole codex view.
//                      Toggled on Open/Close.
//   listContainer    — RectTransform under which entry buttons spawn.
//                      Usually has a VerticalLayoutGroup for auto-flow.
//   entryButtonPrefab— prefab for a single list row. Must contain a
//                      TMP_Text named "Label" (or use the inspector
//                      override) and a Button on the root.
//   titleText        — TMP_Text in the body pane that shows the
//                      selected entry's title.
//   bodyText         — TMP_Text in the body pane that shows the body.
//   closeButton      — clicking it calls Close().
//
// Open() / Close() / Toggle() public methods drive visibility. The
// controller re-populates the list every Open so newly unlocked
// entries appear.
public class LoreCodexUIController : MonoBehaviour
{
    public static LoreCodexUIController Instance { get; private set; }

    [Header("Wire your codex UI here")]
    public GameObject panelRoot;
    public RectTransform listContainer;
    public GameObject entryButtonPrefab;
    public TMP_Text titleText;
    public TMP_Text bodyText;
    public Button closeButton;

    [Header("Optional")]
    [Tooltip("Locked entries display this in place of their title.")]
    public string lockedTitlePlaceholder = "???";
    [Tooltip("Body text when no entry has been recovered yet.")]
    public string emptyBodyText = "No scrolls recovered yet. Search the regions.";
    [Tooltip("Body text when a locked entry is selected.")]
    public string lockedBodyText = "This scroll has not been recovered.";
    [Tooltip("Color tint for locked entries (label/list row).")]
    public Color lockedColor = new Color(0.4f, 0.4f, 0.4f);
    [Tooltip("Color tint for unlocked entries.")]
    public Color unlockedColor = new Color(0.9f, 0.85f, 0.55f);

    private string activeID;

    private void Awake()
    {
        Instance = this;
        if (panelRoot != null) panelRoot.SetActive(false);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (panelRoot != null && panelRoot.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    public void Toggle() { if (panelRoot == null) return; if (panelRoot.activeSelf) Close(); else Open(); }

    public void Open()
    {
        if (panelRoot == null) return;
        panelRoot.SetActive(true);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
        RepopulateList();
    }

    public void Close()
    {
        if (panelRoot == null) return;
        panelRoot.SetActive(false);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
    }

    private void RepopulateList()
    {
        if (listContainer == null || entryButtonPrefab == null) return;

        // Clear out old buttons
        for (int i = listContainer.childCount - 1; i >= 0; i--)
            Destroy(listContainer.GetChild(i).gameObject);

        // Spawn one button per known lore id
        for (int i = 0; i < LoreCodexManager.AllLoreIDs.Length; i++)
        {
            string id = LoreCodexManager.AllLoreIDs[i];
            bool unlocked = LoreCodexManager.IsUnlocked(id);

            GameObject entry = Instantiate(entryButtonPrefab, listContainer);
            entry.SetActive(true);

            // Find label inside prefab — by name 'Label', or first TMP_Text
            TMP_Text label = null;
            foreach (Transform t in entry.GetComponentsInChildren<Transform>(true))
                if (t.name == "Label") { label = t.GetComponent<TMP_Text>(); break; }
            if (label == null) label = entry.GetComponentInChildren<TMP_Text>(true);

            if (label != null)
            {
                label.text = unlocked ? LocalizationManager.Tr(id + "_TITLE") : lockedTitlePlaceholder;
                label.color = unlocked ? unlockedColor : lockedColor;
            }

            Button btn = entry.GetComponent<Button>();
            if (btn != null)
            {
                string idCaptured = id;
                btn.onClick.AddListener(() => Select(idCaptured));
            }
        }

        // Select first unlocked, or show empty state
        bool any = false;
        for (int i = 0; i < LoreCodexManager.AllLoreIDs.Length; i++)
        {
            if (LoreCodexManager.IsUnlocked(LoreCodexManager.AllLoreIDs[i]))
            {
                Select(LoreCodexManager.AllLoreIDs[i]);
                any = true;
                break;
            }
        }
        if (!any)
        {
            if (titleText != null) titleText.text = LocalizationManager.Tr("LORE_EMPTY_TITLE");
            if (bodyText != null) bodyText.text = emptyBodyText;
        }
    }

    public void Select(string id)
    {
        activeID = id;
        bool unlocked = LoreCodexManager.IsUnlocked(id);
        if (titleText != null) titleText.text = unlocked ? LocalizationManager.Tr(id + "_TITLE") : lockedTitlePlaceholder;
        if (bodyText != null) bodyText.text = unlocked ? LocalizationManager.Tr(id + "_BODY") : lockedBodyText;
    }
}
