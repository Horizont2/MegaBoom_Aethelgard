using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Screen shown when the player interacts with the barracks (F key).
// Every widget is a SerializeField reference so the Figma-designed prefab
// can be dropped in and the fields wired by dragging — no code edits.
//
// Layout expectation (matches the Figma mockups):
//   HEADER      — title text + level-pip row (5 helm images) + diamond chip
//   TAB STRIP   — Hire / Upgrade Units / Upgrade Barracks (3 buttons + underlines)
//   HIRE CONTENT — hireRowParent hosts one hireRowPrefab per unit type
//   UPGRADE UNITS CONTENT — upgradeRowParent hosts upgradeRowPrefab per unit
//   UPGRADE BARRACKS CONTENT — single centred layout with diorama, level
//     breakdown, next-level card, resource chip trio, upgrade CTA + build time
public class BarracksUpgradePanel : MonoBehaviour
{
    [Header("Root")]
    public GameObject rootObject;
    public CanvasGroup canvasGroup;
    public Button closeButton;

    [Header("Header")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI diamondsText;
    // Optional — 5 Image slots for the helm level pips at the top right.
    // Each pip gets swapped between the filled and empty sprite based on
    // hostBuilding.currentLevel. Leave empty if the panel doesn't use pips.
    public Image[] barracksLevelPips;
    public Sprite pipHelmFilledSprite;
    public Sprite pipHelmEmptySprite;

    [Header("Custom Unit Portraits")]
    public Sprite portraitMilitia;
    public Sprite portraitRanger;
    public Sprite portraitKnight;

    [Header("Tabs")]
    public Button tabHireButton;
    public Button tabUpgradeUnitsButton;
    public Button tabUpgradeBarracksButton;
    public GameObject hireContainer;
    public GameObject upgradeUnitsContainer;
    public GameObject upgradeBarracksContainer;
    // Optional — thin brass underline images swapped visible/hidden per tab.
    public GameObject tabHireUnderline;
    public GameObject tabUpgradeUnitsUnderline;
    public GameObject tabUpgradeBarracksUnderline;
    // Optional — the darker background rectangle behind the active tab.
    // Wire these if your Figma prefab uses a separate rect for the "active"
    // state instead of a shared underline component.
    public GameObject tabHireActiveBg;
    public GameObject tabUpgradeUnitsActiveBg;
    public GameObject tabUpgradeBarracksActiveBg;
    // Tab-label TMPs so we can swap them between the cream (active) and
    // muted grey (inactive) colours on tab change. Leave null if your Figma
    // prefab uses a single colour for all tabs.
    public TextMeshProUGUI tabHireLabel;
    public TextMeshProUGUI tabUpgradeUnitsLabel;
    public TextMeshProUGUI tabUpgradeBarracksLabel;
    public Color tabActiveTextColor   = new Color(0.94f, 0.89f, 0.80f, 1f); // warm cream #F0E4CB
    public Color tabInactiveTextColor = new Color(0.54f, 0.52f, 0.47f, 1f); // muted grey #8A8478
    // Tint applied to each tab Button's targetGraphic while active — makes
    // the active tab look "pressed in" even after the pointer leaves.
    // Transparent white = no tint, so inactive tabs blend into the panel.
    public Color tabActiveButtonTint   = new Color(0f, 0f, 0f, 0.38f);
    public Color tabInactiveButtonTint = new Color(1f, 1f, 1f, 0f);

    [Header("Hire Tab")]
    public Transform hireRowParent;
    public GameObject hireRowPrefab; // exposes BarracksHireRow fields
    public MercenaryRoster roster;   // fallback if singleton not ready

    [Header("Upgrade Units Tab")]
    public Transform upgradeRowParent;
    public GameObject upgradeRowPrefab; // exposes BarracksUpgradeUnitRow fields
    // Shared pip sprites for the per-unit level rows — the row prefab reads
    // these from the panel so we don't have to reassign them per row instance.
    public Sprite unitPipFilledSprite;
    public Sprite unitPipEmptySprite;

    [Header("Upgrade Barracks Tab")]
    public CampBuilding hostBuilding;  // the CampBuilding driving level state
    // Left column
    public Image barracksDioramaImage;
    public TextMeshProUGUI barracksCurrentLevelText;   // "LEVEL 3 / 5"
    public TextMeshProUGUI barracksSummaryText;        // "MAX SIZE 5 UNITS · KNIGHT TIER UNLOCKED"
    // Right column — next level card
    public TextMeshProUGUI barracksNextLevelText;      // "LEVEL 4"
    public TextMeshProUGUI barracksPerksText;          // multi-line perks
    // Right column — cost chip trio
    public TextMeshProUGUI barracksCostWoodText;
    public TextMeshProUGUI barracksCostStoneText;
    public TextMeshProUGUI barracksCostFoodText;
    // Right column — CTA
    public Button barracksUpgradeButton;
    public TextMeshProUGUI barracksUpgradeButtonText;  // "UPGRADE"
    public TextMeshProUGUI barracksBuildTimeText;      // "4:00"
    // Colour used for cost numbers when the player can't afford them.
    public Color costUnaffordableColor = new Color(0.70f, 0.37f, 0.32f, 1f); // muted rust #B25E52
    public Color costAffordableColor   = new Color(0.94f, 0.89f, 0.80f, 1f); // warm cream

    [Header("Optional Per-Level Overrides (Barracks Upgrade Tab)")]
    // Designer can override the auto-derived perks text per level here.
    // Element 0 = perks displayed when currentLevel = 0 (going to Lv 1),
    // Element 4 = when at Lv 5 (max level → panel hides the CTA).
    [TextArea(2, 4)] public string[] perksTextPerLevel = new string[5];
    // Same idea for the max-cap summary line under the current-level text.
    [TextArea(1, 2)] public string[] summaryTextPerLevel = new string[6];

    // Runtime bookkeeping so we don't rebuild row lists every frame.
    private readonly List<GameObject> spawnedHireRows = new List<GameObject>();
    private readonly List<GameObject> spawnedUpgradeRows = new List<GameObject>();

    // Static flag other systems check to know a modal panel is up:
    //  · CameraFollow skips mouse-look while open (mouse is used to click UI)
    //  · GlobalHUD's ESC handler closes the panel before opening pause
    // Set by Open()/Close() so it always tracks the visible state.
    public static bool IsOpen { get; private set; }

    // Cursor state we save on Open() and restore on Close() so we don't
    // permanently un-hide the cursor in scenes where it was already locked
    // (e.g. gameplay).
    private CursorLockMode savedCursorLock;
    private bool savedCursorVisible;
    private bool cursorStateSaved;

    private void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (tabHireButton != null) tabHireButton.onClick.AddListener(() => ShowTab(0));
        if (tabUpgradeUnitsButton != null) tabUpgradeUnitsButton.onClick.AddListener(() => ShowTab(1));
        if (tabUpgradeBarracksButton != null) tabUpgradeBarracksButton.onClick.AddListener(() => ShowTab(2));
        if (barracksUpgradeButton != null) barracksUpgradeButton.onClick.AddListener(OnUpgradeBarracks);

        if (rootObject != null) rootObject.SetActive(false);
    }

    private void OnEnable()
    {
        MercenaryRoster.OnRosterChanged += Refresh;
    }

    private void OnDisable()
    {
        MercenaryRoster.OnRosterChanged -= Refresh;
    }

    public void Open()
    {
        if (rootObject == null)
        {
            Debug.LogWarning("[BarracksUpgradePanel] `rootObject` field is not wired — nothing to show. Drag the panel root GameObject here.");
            return;
        }
        Transform t = rootObject.transform;
        while (t != null)
        {
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            t = t.parent;
        }
        rootObject.SetActive(true);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
        if (titleText != null) titleText.text = LocalizationManager.Tr("MERC_BARRACKS_TITLE");
        ShowTab(0);
        Refresh();

        // Snapshot cursor state and force the pointer visible so the player
        // can actually click the buttons.
        if (!cursorStateSaved)
        {
            savedCursorLock = Cursor.lockState;
            savedCursorVisible = Cursor.visible;
            cursorStateSaved = true;
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        IsOpen = true;
    }

    public void Close()
    {
        if (rootObject != null) rootObject.SetActive(false);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        // Lock the cursor back to gameplay mode. The user explicitly wants
        // the pointer to vanish after every close, regardless of what it
        // was before — matches the notice-board convention.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        cursorStateSaved = false;
        IsOpen = false;
    }

    private void ShowTab(int idx)
    {
        if (hireContainer != null) hireContainer.SetActive(idx == 0);
        if (upgradeUnitsContainer != null) upgradeUnitsContainer.SetActive(idx == 1);
        if (upgradeBarracksContainer != null) upgradeBarracksContainer.SetActive(idx == 2);

        if (tabHireUnderline != null) tabHireUnderline.SetActive(idx == 0);
        if (tabUpgradeUnitsUnderline != null) tabUpgradeUnitsUnderline.SetActive(idx == 1);
        if (tabUpgradeBarracksUnderline != null) tabUpgradeBarracksUnderline.SetActive(idx == 2);

        if (tabHireActiveBg != null) tabHireActiveBg.SetActive(idx == 0);
        if (tabUpgradeUnitsActiveBg != null) tabUpgradeUnitsActiveBg.SetActive(idx == 1);
        if (tabUpgradeBarracksActiveBg != null) tabUpgradeBarracksActiveBg.SetActive(idx == 2);

        if (tabHireLabel != null)             tabHireLabel.color             = idx == 0 ? tabActiveTextColor : tabInactiveTextColor;
        if (tabUpgradeUnitsLabel != null)     tabUpgradeUnitsLabel.color     = idx == 1 ? tabActiveTextColor : tabInactiveTextColor;
        if (tabUpgradeBarracksLabel != null)  tabUpgradeBarracksLabel.color  = idx == 2 ? tabActiveTextColor : tabInactiveTextColor;

        // Persistent "pressed" look for the active tab — bake the pressed
        // colour into the Button's ColorBlock so hover/normal transitions
        // don't wash it out.
        ApplyTabPressedLook(tabHireButton,            idx == 0);
        ApplyTabPressedLook(tabUpgradeUnitsButton,    idx == 1);
        ApplyTabPressedLook(tabUpgradeBarracksButton, idx == 2);
    }

    private void ApplyTabPressedLook(Button btn, bool active)
    {
        if (btn == null) return;

        // Перевіряємо, чи кнопка використовує Sprite Swap
        if (btn.transition == Selectable.Transition.SpriteSwap)
        {
            if (btn.targetGraphic is Image img)
            {
                // Скидаємо колір на білий, щоб неактивні кнопки не ставали прозорими
                img.color = Color.white;
            }

            // Щоб зберегти стан "натиснутої" вкладки у режимі Sprite Swap,
            // ми робимо активну вкладку неінтерактивною.
            // ВАЖЛИВО: В Інспекторі признач свій спрайт "Pressed" у слот "Disabled Sprite"!
            btn.interactable = !active;
        }
        // Оригінальна логіка для режиму Color Tint
        else if (btn.transition == Selectable.Transition.ColorTint)
        {
            btn.interactable = true; // Повертаємо інтерактивність
            Color tint = active ? tabActiveButtonTint : tabInactiveButtonTint;

            var cb = btn.colors;
            cb.normalColor = tint;
            cb.highlightedColor = active
                ? Color.Lerp(tint, new Color(1f, 1f, 1f, tint.a), 0.15f)
                : new Color(1f, 1f, 1f, 0.06f);
            cb.pressedColor = tint;
            cb.selectedColor = tint;
            btn.colors = cb;

            if (btn.targetGraphic is Image img)
            {
                img.color = tint;
            }
        }
    }

    // Poll diamond total so the panel refreshes when the player spends
    // gold via another window (or via hiring in this same panel — Hire()
    // triggers OnRosterChanged which also calls Refresh, but the cost
    // colours on OTHER rows need to update too). Also handle ESC to close.
    private int lastKnownDiamonds = -1;
    private void Update()
    {
        // Invariant sanity: rootObject.active <=> IsOpen. GlobalHUD's
        // pause-exit re-activates every panel in `gameplayPanels`, which
        // was silently re-showing the barracks after a pause even if the
        // player had it closed. If the root is visible but our IsOpen is
        // false, force-hide.
        if (rootObject != null && rootObject.activeSelf && !IsOpen)
        {
            rootObject.SetActive(false);
            if (canvasGroup != null) { canvasGroup.alpha = 0f; canvasGroup.interactable = false; canvasGroup.blocksRaycasts = false; }
            return;
        }

        if (rootObject != null && !rootObject.activeSelf) return;

        // ESC closes the panel — must run BEFORE GlobalHUD's own ESC handler
        // fires TogglePause. GlobalHUD checks BarracksUpgradePanel.IsOpen
        // and swallows the ESC when true, so this branch stays authoritative.
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        if (ResourceManager.Instance == null) return;
        if (ResourceManager.Instance.diamonds != lastKnownDiamonds)
        {
            lastKnownDiamonds = ResourceManager.Instance.diamonds;
            Refresh();
        }
    }

    private void Refresh()
    {
        var r = roster != null ? roster : MercenaryRoster.Instance;
        if (r == null) return;

        if (diamondsText != null && ResourceManager.Instance != null)
            diamondsText.text = ResourceManager.Instance.diamonds.ToString();

        RefreshHeaderPips();
        RefreshHireTab(r);
        RefreshUpgradeTab(r);
        RefreshBarracksTab();
    }

    private void RefreshHeaderPips()
    {
        if (barracksLevelPips == null || barracksLevelPips.Length == 0) return;
        // Pips now represent army slots — filled per alive mercenary, empty
        // per free slot. Slot cap == pip array length (drag more Image slots
        // into the array to raise the visual cap).
        int alive = MercenaryRoster.Instance != null ? MercenaryRoster.Instance.CountAliveTotal() : 0;
        for (int i = 0; i < barracksLevelPips.Length; i++)
        {
            var img = barracksLevelPips[i];
            if (img == null) continue;
            img.sprite = (i < alive) ? pipHelmFilledSprite : pipHelmEmptySprite;
        }
    }

    // --------- Hire ---------

    private void RefreshHireTab(MercenaryRoster r)
    {
        if (hireRowParent == null || hireRowPrefab == null) return;
        int hostLevel = hostBuilding != null ? hostBuilding.currentLevel : 1;

        EnsureRowCount(spawnedHireRows, hireRowParent, hireRowPrefab, r.catalogue.Count);

        for (int i = 0; i < r.catalogue.Count; i++)
        {
            var data = r.catalogue[i];
            var go = spawnedHireRows[i];
            if (data == null || go == null) continue;

            var row = go.GetComponent<BarracksHireRow>();
            if (row == null) row = go.AddComponent<BarracksHireRow>();

            // Визначаємо портрет за індексом юніта (0=Militia, 1=Ranger, 2=Knight)
            Sprite customPortrait = i == 0 ? portraitMilitia : (i == 1 ? portraitRanger : portraitKnight);

            row.Bind(data, r, hostLevel, customPortrait);
        }
    }

    // --------- Upgrade Units ---------

    private void RefreshUpgradeTab(MercenaryRoster r)
    {
        if (upgradeRowParent == null || upgradeRowPrefab == null) return;

        EnsureRowCount(spawnedUpgradeRows, upgradeRowParent, upgradeRowPrefab, r.catalogue.Count);

        for (int i = 0; i < r.catalogue.Count; i++)
        {
            var data = r.catalogue[i];
            var go = spawnedUpgradeRows[i];
            if (data == null || go == null) continue;

            var row = go.GetComponent<BarracksUpgradeUnitRow>();
            if (row == null) row = go.AddComponent<BarracksUpgradeUnitRow>();

            Sprite customPortrait = i == 0 ? portraitMilitia : (i == 1 ? portraitRanger : portraitKnight);

            row.Bind(data, r, this, customPortrait);
        }
    }

    // --------- Upgrade Barracks ---------

    private void RefreshBarracksTab()
    {
        if (hostBuilding == null) return;
        int level = hostBuilding.currentLevel;
        int maxLevel = hostBuilding.levels != null ? hostBuilding.levels.Length : 0;

        if (barracksCurrentLevelText != null) barracksCurrentLevelText.text = LocalizationManager.Tr("MERC_LEVEL_XY", level, maxLevel);
        if (barracksSummaryText != null)
        {
            barracksSummaryText.text = ResolveSummaryText(level);
        }

        bool atMax = level >= maxLevel;

        if (atMax)
        {
            if (barracksNextLevelText != null) barracksNextLevelText.text = LocalizationManager.Tr("MERC_MAX_LEVEL");
            if (barracksPerksText != null) barracksPerksText.text = LocalizationManager.Tr("MERC_PERKS_MAXED");
            SetCostText(barracksCostWoodText, 0, true);
            SetCostText(barracksCostStoneText, 0, true);
            SetCostText(barracksCostFoodText, 0, true);
            if (barracksUpgradeButton != null) barracksUpgradeButton.interactable = false;
            if (barracksUpgradeButtonText != null) barracksUpgradeButtonText.text = LocalizationManager.Tr("MERC_BTN_MAX");
            if (barracksBuildTimeText != null) barracksBuildTimeText.text = "—";
            return;
        }

        var next = hostBuilding.levels[level];
        if (barracksNextLevelText != null) barracksNextLevelText.text = LocalizationManager.Tr("MERC_LEVEL_HEADER", level + 1);
        if (barracksPerksText != null)
        {
            barracksPerksText.text = ResolvePerksText(level, next);
        }

        bool canAfford = ResourceManager.Instance != null &&
                         ResourceManager.Instance.CanAffordStash(next.costWood, next.costStone, next.costFood);

        SetCostText(barracksCostWoodText, next.costWood,
            ResourceManager.Instance != null && ResourceManager.Instance.stashWood >= next.costWood);
        SetCostText(barracksCostStoneText, next.costStone,
            ResourceManager.Instance != null && ResourceManager.Instance.stashStone >= next.costStone);
        SetCostText(barracksCostFoodText, next.costFood,
            ResourceManager.Instance != null && ResourceManager.Instance.stashFood >= next.costFood);

        if (barracksUpgradeButton != null) barracksUpgradeButton.interactable = canAfford;
        if (barracksUpgradeButtonText != null) barracksUpgradeButtonText.text = LocalizationManager.Tr("MERC_BTN_UPGRADE");
        if (barracksBuildTimeText != null) barracksBuildTimeText.text = FormatBuildTime(next.buildTime);
    }

    private string ResolveSummaryText(int level)
    {
        if (summaryTextPerLevel != null && level >= 0 && level < summaryTextPerLevel.Length &&
            !string.IsNullOrEmpty(summaryTextPerLevel[level]))
        {
            return summaryTextPerLevel[level];
        }
        // Fallback: build a summary from the CampBuilding's current-level description.
        if (hostBuilding != null && hostBuilding.levels != null &&
            level > 0 && level - 1 < hostBuilding.levels.Length)
        {
            var lv = hostBuilding.levels[level - 1];
            if (lv != null && !string.IsNullOrEmpty(lv.productionDescription))
                return LocalizationManager.Tr(lv.productionDescription).ToUpper();
        }
        return string.Empty;
    }

    private string ResolvePerksText(int level, BuildingLevel next)
    {
        if (perksTextPerLevel != null && level >= 0 && level < perksTextPerLevel.Length &&
            !string.IsNullOrEmpty(perksTextPerLevel[level]))
        {
            return LocalizationManager.Tr(perksTextPerLevel[level]);
        }
        // Fallback: use the CampBuilding-level's own productionDescription
        // formatted with the Figma design's brass diamond bullet.
        if (next != null && !string.IsNullOrEmpty(next.productionDescription))
            return "◆ " + LocalizationManager.Tr(next.productionDescription);
        return string.Empty;
    }

    private void SetCostText(TextMeshProUGUI target, int value, bool affordable)
    {
        if (target == null) return;
        target.text = value.ToString();
        target.color = affordable ? costAffordableColor : costUnaffordableColor;
    }

    private string FormatBuildTime(float sec)
    {
        int total = Mathf.RoundToInt(sec);
        int m = total / 60;
        int s = total % 60;
        return m > 0 ? $"{m}:{s:D2}" : $"0:{s:D2}";
    }

    private void OnUpgradeBarracks()
    {
        if (hostBuilding == null || ResourceManager.Instance == null) return;
        int level = hostBuilding.currentLevel;
        if (hostBuilding.levels == null || level >= hostBuilding.levels.Length) return;

        var next = hostBuilding.levels[level];
        if (!ResourceManager.Instance.CanAffordStash(next.costWood, next.costStone, next.costFood)) return;

        // Route through CampBuilding's timed upgrade path so the ghost
        // model + dust + tracked-upgrade HUD widget behave like every
        // other building. Previously this method spent resources and
        // set currentLevel = level+1 in the same frame — instant
        // completion, no dust, no ghost, no timer widget.
        ResourceManager.Instance.SpendStashResources(next.costWood, next.costStone, next.costFood);

        long startTimeBinary = System.DateTime.UtcNow.ToBinary();
        PlayerPrefs.SetString("UpgradeStart_" + hostBuilding.buildingID, startTimeBinary.ToString());
        PlayerPrefs.SetInt("IsUpgrading_" + hostBuilding.buildingID, 1);
        PlayerPrefs.Save();

        hostBuilding.StartDustEffect();
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX3D(AudioID.Camp_BuildStart, hostBuilding.transform.position);

        if (GlobalHUD.Instance != null)
            GlobalHUD.Instance.StartTrackingUpgrade(hostBuilding.buildingID, hostBuilding.buildingName,
                                                    hostBuilding.buildingIconSprite, next.buildTime, startTimeBinary);

        Refresh();
    }

    // --------- Utils ---------

    private void EnsureRowCount(List<GameObject> list, Transform parent, GameObject prefab, int wanted)
    {
        if (parent == null || prefab == null) return;

        // Guard against a mis-wired inspector reference: if the parent
        // Transform lives inside a Project prefab asset (not the scene) then
        // Instantiate throws "Cannot instantiate objects with a parent which
        // is persistent". Usually means the panel was dragged into scene
        // but the row-parent field was wired to the source prefab's inner
        // Transform instead of the scene instance's own child.
        if (!parent.gameObject.scene.IsValid())
        {
            Debug.LogError("[BarracksUpgradePanel] A row-parent Transform on '" + name + "' points at a PREFAB ASSET, not a scene child. Open the panel scene instance in Hierarchy, find the row-parent GameObject inside it, and drag THAT into the field.");
            return;
        }

        while (list.Count < wanted)
        {
            var go = Instantiate(prefab, parent);
            list.Add(go);
        }
        while (list.Count > wanted)
        {
            var last = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            if (last != null) Destroy(last);
        }
    }

    // Row prefabs call these to fetch the shared pip sprites off the panel
    // instead of duplicating references on every row.
    public Sprite GetUnitPipFilledSprite() => unitPipFilledSprite;
    public Sprite GetUnitPipEmptySprite() => unitPipEmptySprite;
}
