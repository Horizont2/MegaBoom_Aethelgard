using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// Persistent codex of lore fragments the player has unlocked. Entries
// are seeded in LocalizationManager (LORE_* keys with _TITLE and _BODY
// pairs). ScrollPickup unlocks them in-world; the codex panel reads
// from here.
public static class LoreCodexManager
{
    // The canonical lore IDs — must match the keys seeded in LocalizationManager.
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
    };

    private const string UnlockedPrefix = "Lore_Unlocked_";

    public static System.Action<string> OnLoreUnlocked;

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

// Drop this on a scroll prefab in-world. When the player walks into
// trigger range, press [E] to claim the lore entry. The pickup
// destroys itself once claimed.
[RequireComponent(typeof(Collider))]
public class ScrollPickup : MonoBehaviour
{
    [Tooltip("LocalizationManager LORE_* id to unlock when this scroll is picked up.")]
    public string loreID = "LORE_AETHELGARD_FALL";
    [Tooltip("Auto-unlock on trigger (true) or require [E] interaction (false).")]
    public bool autoUnlockOnTouch = true;

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
        else if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt("[E] Read scroll");
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

// Procedural codex viewer UI — auto-builds when LoreCodexUI.Open is
// called. Lists every known LORE_* id with title, marks locked
// entries as "???" and renders the body of unlocked ones in a
// scrollable right pane.
public class LoreCodexUI : MonoBehaviour
{
    private static LoreCodexUI s_instance;

    private GameObject panel;
    private TextMeshProUGUI titleLabel;
    private TextMeshProUGUI bodyLabel;
    private RectTransform listContainer;
    private string activeID;

    public static void Open()
    {
        Get().BuildIfNeeded();
        Get().panel.SetActive(true);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
    }

    public static void Close()
    {
        if (s_instance == null || s_instance.panel == null) return;
        s_instance.panel.SetActive(false);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
    }

    private static LoreCodexUI Get()
    {
        if (s_instance != null) return s_instance;
        GameObject go = new GameObject("[LoreCodexUI]");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<LoreCodexUI>();
        return s_instance;
    }

    private void BuildIfNeeded()
    {
        if (panel != null) return;
        if (GlobalHUD.Instance == null) return;
        RectTransform hudRect = GlobalHUD.Instance.GetComponent<RectTransform>();
        if (hudRect == null) return;

        // Backdrop dim
        panel = new GameObject("LoreCodexPanel");
        RectTransform pRT = panel.AddComponent<RectTransform>();
        pRT.SetParent(hudRect, false);
        pRT.anchorMin = Vector2.zero;
        pRT.anchorMax = Vector2.one;
        pRT.offsetMin = Vector2.zero;
        pRT.offsetMax = Vector2.zero;
        Image dim = panel.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.78f);
        dim.raycastTarget = true;

        // Window
        GameObject win = new GameObject("Window");
        RectTransform winRT = win.AddComponent<RectTransform>();
        winRT.SetParent(pRT, false);
        winRT.anchorMin = new Vector2(0.5f, 0.5f);
        winRT.anchorMax = new Vector2(0.5f, 0.5f);
        winRT.pivot = new Vector2(0.5f, 0.5f);
        winRT.sizeDelta = new Vector2(1000f, 600f);
        Image winBg = win.AddComponent<Image>();
        winBg.color = new Color(0.13f, 0.10f, 0.07f, 0.96f);

        // Title at top
        GameObject hdr = new GameObject("Header");
        RectTransform hdrRT = hdr.AddComponent<RectTransform>();
        hdrRT.SetParent(winRT, false);
        hdrRT.anchorMin = new Vector2(0f, 1f);
        hdrRT.anchorMax = new Vector2(1f, 1f);
        hdrRT.pivot = new Vector2(0.5f, 1f);
        hdrRT.sizeDelta = new Vector2(0f, 50f);
        TextMeshProUGUI hdrTxt = hdr.AddComponent<TextMeshProUGUI>();
        hdrTxt.text = "CHRONICLES OF AETHELGARD";
        hdrTxt.fontSize = 26f;
        hdrTxt.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
        hdrTxt.alignment = TextAlignmentOptions.Center;
        hdrTxt.color = new Color(0.85f, 0.7f, 0.4f);
        hdrTxt.outlineWidth = 0.18f;
        hdrTxt.outlineColor = Color.black;

        // Left list
        GameObject list = new GameObject("List");
        RectTransform listRT = list.AddComponent<RectTransform>();
        listRT.SetParent(winRT, false);
        listRT.anchorMin = new Vector2(0f, 0f);
        listRT.anchorMax = new Vector2(0.4f, 1f);
        listRT.offsetMin = new Vector2(20f, 60f);
        listRT.offsetMax = new Vector2(-10f, -60f);
        VerticalLayoutGroup vlg = list.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6f;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        listContainer = listRT;

        // Right body
        GameObject body = new GameObject("Body");
        RectTransform bodyRT = body.AddComponent<RectTransform>();
        bodyRT.SetParent(winRT, false);
        bodyRT.anchorMin = new Vector2(0.4f, 0f);
        bodyRT.anchorMax = new Vector2(1f, 1f);
        bodyRT.offsetMin = new Vector2(10f, 60f);
        bodyRT.offsetMax = new Vector2(-20f, -60f);

        GameObject titleGO = new GameObject("Title");
        RectTransform tRT = titleGO.AddComponent<RectTransform>();
        tRT.SetParent(bodyRT, false);
        tRT.anchorMin = new Vector2(0f, 1f);
        tRT.anchorMax = new Vector2(1f, 1f);
        tRT.pivot = new Vector2(0.5f, 1f);
        tRT.sizeDelta = new Vector2(0f, 40f);
        titleLabel = titleGO.AddComponent<TextMeshProUGUI>();
        titleLabel.fontSize = 22f;
        titleLabel.fontStyle = FontStyles.Bold;
        titleLabel.alignment = TextAlignmentOptions.MidlineLeft;
        titleLabel.color = new Color(1f, 0.9f, 0.55f);

        GameObject bodyTxtGO = new GameObject("BodyText");
        RectTransform btRT = bodyTxtGO.AddComponent<RectTransform>();
        btRT.SetParent(bodyRT, false);
        btRT.anchorMin = new Vector2(0f, 0f);
        btRT.anchorMax = new Vector2(1f, 1f);
        btRT.offsetMin = Vector2.zero;
        btRT.offsetMax = new Vector2(0f, -50f);
        bodyLabel = bodyTxtGO.AddComponent<TextMeshProUGUI>();
        bodyLabel.fontSize = 15f;
        bodyLabel.alignment = TextAlignmentOptions.TopLeft;
        bodyLabel.color = new Color(0.9f, 0.86f, 0.78f);

        // Close button (top right)
        GameObject btn = new GameObject("Close");
        RectTransform btnRT = btn.AddComponent<RectTransform>();
        btnRT.SetParent(winRT, false);
        btnRT.anchorMin = new Vector2(1f, 1f);
        btnRT.anchorMax = new Vector2(1f, 1f);
        btnRT.pivot = new Vector2(1f, 1f);
        btnRT.anchoredPosition = new Vector2(-10f, -10f);
        btnRT.sizeDelta = new Vector2(34f, 34f);
        Image btnImg = btn.AddComponent<Image>();
        btnImg.color = new Color(0.3f, 0.1f, 0.1f, 0.85f);
        Button btnBtn = btn.AddComponent<Button>();
        btnBtn.onClick.AddListener(Close);
        GameObject btnTxtGO = new GameObject("X");
        RectTransform btxRT = btnTxtGO.AddComponent<RectTransform>();
        btxRT.SetParent(btnRT, false);
        btxRT.anchorMin = Vector2.zero;
        btxRT.anchorMax = Vector2.one;
        btxRT.offsetMin = Vector2.zero;
        btxRT.offsetMax = Vector2.zero;
        TextMeshProUGUI bx = btnTxtGO.AddComponent<TextMeshProUGUI>();
        bx.text = "X";
        bx.fontSize = 22f;
        bx.alignment = TextAlignmentOptions.Center;
        bx.color = Color.white;

        // Populate list
        BuildList();

        panel.SetActive(false);
    }

    private void BuildList()
    {
        foreach (Transform t in listContainer) Destroy(t.gameObject);

        for (int i = 0; i < LoreCodexManager.AllLoreIDs.Length; i++)
        {
            string id = LoreCodexManager.AllLoreIDs[i];
            bool unlocked = LoreCodexManager.IsUnlocked(id);

            GameObject entry = new GameObject("Entry_" + i);
            RectTransform eRT = entry.AddComponent<RectTransform>();
            eRT.SetParent(listContainer, false);
            LayoutElement le = entry.AddComponent<LayoutElement>();
            le.minHeight = 32f;
            Image bg = entry.AddComponent<Image>();
            bg.color = unlocked ? new Color(0.2f, 0.17f, 0.12f, 0.7f) : new Color(0.10f, 0.10f, 0.10f, 0.6f);

            GameObject txtGO = new GameObject("Label");
            RectTransform tRT = txtGO.AddComponent<RectTransform>();
            tRT.SetParent(eRT, false);
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.offsetMin = new Vector2(8f, 4f);
            tRT.offsetMax = new Vector2(-8f, -4f);
            TextMeshProUGUI tmp = txtGO.AddComponent<TextMeshProUGUI>();
            tmp.text = unlocked ? LocalizationManager.Tr(id + "_TITLE") : "???";
            tmp.fontSize = 15f;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = unlocked ? new Color(0.9f, 0.85f, 0.55f) : new Color(0.4f, 0.4f, 0.4f);

            string idCaptured = id;
            Button btn = entry.AddComponent<Button>();
            btn.onClick.AddListener(() => Select(idCaptured));
        }

        if (LoreCodexManager.UnlockedCount > 0)
        {
            for (int i = 0; i < LoreCodexManager.AllLoreIDs.Length; i++)
            {
                if (LoreCodexManager.IsUnlocked(LoreCodexManager.AllLoreIDs[i]))
                {
                    Select(LoreCodexManager.AllLoreIDs[i]);
                    break;
                }
            }
        }
        else
        {
            titleLabel.text = "—";
            bodyLabel.text = "No scrolls recovered yet. Search the regions.";
        }
    }

    private void Select(string id)
    {
        activeID = id;
        if (LoreCodexManager.IsUnlocked(id))
        {
            titleLabel.text = LocalizationManager.Tr(id + "_TITLE");
            bodyLabel.text = LocalizationManager.Tr(id + "_BODY");
        }
        else
        {
            titleLabel.text = "???";
            bodyLabel.text = "This scroll has not been recovered.";
        }
    }

    private void Update()
    {
        if (panel != null && panel.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Close();
    }
}
