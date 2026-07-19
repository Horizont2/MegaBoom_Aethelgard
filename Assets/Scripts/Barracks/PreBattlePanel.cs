using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Pre-battle screen for a region without a totem. Opened by MapPanelUI when
// the player picks a mercenary-only region. Every widget is a SerializeField
// reference so the panel prefab can be redesigned freely.
public class PreBattlePanel : MonoBehaviour
{
    [Header("Root")]
    public GameObject rootObject;
    public CanvasGroup canvasGroup;
    public Button closeButton;

    [Header("Header")]
    // Static "ARMY DEPLOYMENT" title — script writes it on Open.
    public TextMeshProUGUI titleText;
    // 5 helm level pips like the barracks panel — decorative, filled up to
    // the barracks' current level to show upgrade rank at a glance.
    public Image[] barracksLevelPips;
    public Sprite pipHelmFilledSprite;
    public Sprite pipHelmEmptySprite;

    [Header("Region Info")]
    public TextMeshProUGUI regionNameText;
    public TextMeshProUGUI enemyStrengthText;   // legacy — kept for old prefabs
    public TextMeshProUGUI enemyPowerText;      // NEW mockup uses this label
    public TextMeshProUGUI travelTimeText;

    [Header("Unit Selection List")]
    public Transform unitRowParent;
    public GameObject unitRowPrefab; // needs PreBattleUnitRow component
    // Optional per-unit portrait overrides (same convention as barracks panel).
    public Sprite portraitMilitia;
    public Sprite portraitRanger;
    public Sprite portraitKnight;

    [Header("Tactic Selection (optional — omit for mockup layout)")]
    public Button tacticAmbushButton;
    public Button tacticAssaultButton;
    public Button tacticSiegeButton;
    public Image tacticAmbushHighlight;
    public Image tacticAssaultHighlight;
    public Image tacticSiegeHighlight;

    [Header("Forecast (legacy — kept for older prefabs)")]
    public TextMeshProUGUI riskBandText;
    public TextMeshProUGUI expectedCasualtiesText;
    public TextMeshProUGUI armyScoreText;

    [Header("Forecast (new: Win Probability)")]
    // Text label inside the circular gauge — "85%" etc.
    public TextMeshProUGUI winProbabilityText;
    // Radial-filled Image whose fillAmount is set to winChance 0-1 so the
    // ring visualises the chance.
    public Image winProbabilityFillImage;
    // Colours applied to both the text and the ring based on the risk band.
    public Color winColorHigh   = new Color(0.55f, 0.85f, 0.55f, 1f); // green
    public Color winColorMid    = new Color(0.90f, 0.75f, 0.35f, 1f); // amber
    public Color winColorLow    = new Color(0.85f, 0.42f, 0.32f, 1f); // rust

    [Header("Confirm")]
    public Button confirmButton;
    public TextMeshProUGUI confirmButtonText;
    // Optional secondary CANCEL button (mockup has one under DEPLOY ARMY).
    public Button cancelButton;

    [Header("Tactic Description (optional)")]
    // TMP that displays a short pros/cons line for the currently selected
    // tactic. Refreshed in SetTactic. Leave null if the panel doesn't have
    // a tooltip slot.
    public TextMeshProUGUI tacticDescriptionText;

    [Header("Tactic Preview (optional)")]
    // Big illustration slot that always shows the current tactic. Used
    // when the panel has one central image (instead of three per-button
    // highlights). Wire this + three sprites in the Inspector.
    public Image tacticPreviewImage;
    public Sprite tacticAmbushSprite;
    public Sprite tacticAssaultSprite;
    public Sprite tacticSiegeSprite;
    // Placeholder shown while no tactic is chosen — usually the default
    // Assault sprite. Falls back to tacticAssaultSprite if left null.
    public Sprite tacticDefaultSprite;

    private RegionData currentRegion;
    private CampaignTactic currentTactic = CampaignTactic.Assault;
    private readonly Dictionary<string, int> desiredCounts = new Dictionary<string, int>();
    private readonly List<GameObject> spawnedUnitRows = new List<GameObject>();

    private void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (cancelButton != null) cancelButton.onClick.AddListener(Close);
        if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirm);

        if (tacticAmbushButton != null) tacticAmbushButton.onClick.AddListener(() => SetTactic(CampaignTactic.Ambush));
        if (tacticAssaultButton != null) tacticAssaultButton.onClick.AddListener(() => SetTactic(CampaignTactic.Assault));
        if (tacticSiegeButton != null) tacticSiegeButton.onClick.AddListener(() => SetTactic(CampaignTactic.Siege));

        if (rootObject != null) rootObject.SetActive(false);
    }

    public void Open(RegionData region)
    {
        currentRegion = region;
        desiredCounts.Clear();
        currentTactic = CampaignTactic.Assault;

        if (rootObject != null) rootObject.SetActive(true);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        if (titleText != null) titleText.text = LocalizationManager.Tr("MERC_ARMY_DEPLOYMENT");
        if (regionNameText != null) regionNameText.text = LocalizationManager.Tr(region.regionName).ToUpper();
        if (enemyStrengthText != null) enemyStrengthText.text = LocalizationManager.Tr("MERC_ENEMY_STRENGTH", region.enemyStrength);
        if (enemyPowerText != null) enemyPowerText.text = LocalizationManager.Tr("MERC_ENEMY_POWER", region.enemyStrength);

        RefreshBarracksPips();

        // Guarantee the preview slot shows SOMETHING the moment the panel
        // opens, even before SetTactic runs — otherwise there's a one-frame
        // "empty rectangle" flash before the default tactic swap kicks in.
        if (tacticPreviewImage != null)
        {
            Sprite fallback = tacticDefaultSprite != null ? tacticDefaultSprite : tacticAssaultSprite;
            if (fallback != null) tacticPreviewImage.sprite = fallback;
            tacticPreviewImage.enabled = tacticPreviewImage.sprite != null;
        }

        SetTactic(CampaignTactic.Assault);
        RebuildUnitRows();
        Refresh();
    }

    private void RefreshBarracksPips()
    {
        if (barracksLevelPips == null || barracksLevelPips.Length == 0) return;
        // Pips reflect current alive army size, not barracks upgrade level —
        // showing the barracks lvl on this deployment screen was pointless
        // since the player already saw it in the barracks panel. Filled per
        // alive unit, empty per free slot.
        int alive = MercenaryRoster.Instance != null ? MercenaryRoster.Instance.CountAliveTotal() : 0;
        for (int i = 0; i < barracksLevelPips.Length; i++)
        {
            var img = barracksLevelPips[i];
            if (img == null) continue;
            img.sprite = (i < alive) ? pipHelmFilledSprite : pipHelmEmptySprite;
        }
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
    }

    // Colour applied to the active tactic button's Image (ColorTint transition).
    // Same pattern as the barracks tab tint.
    public Color tacticActiveButtonTint   = new Color(0f, 0f, 0f, 0.38f);
    public Color tacticInactiveButtonTint = new Color(1f, 1f, 1f, 0f);

    // SpriteSwap cache — first time we touch each tactic button we snapshot
    // its resting sprite so we can restore it when the tactic becomes inactive.
    private readonly System.Collections.Generic.Dictionary<Button, Sprite> defaultTacticSprites =
        new System.Collections.Generic.Dictionary<Button, Sprite>();

    private void SetTactic(CampaignTactic t)
    {
        currentTactic = t;
        if (tacticAmbushHighlight != null) tacticAmbushHighlight.enabled = t == CampaignTactic.Ambush;
        if (tacticAssaultHighlight != null) tacticAssaultHighlight.enabled = t == CampaignTactic.Assault;
        if (tacticSiegeHighlight != null) tacticSiegeHighlight.enabled = t == CampaignTactic.Siege;

        // Persistent "pressed" look for the active tactic button.
        ApplyTacticPressedLook(tacticAmbushButton,  t == CampaignTactic.Ambush);
        ApplyTacticPressedLook(tacticAssaultButton, t == CampaignTactic.Assault);
        ApplyTacticPressedLook(tacticSiegeButton,   t == CampaignTactic.Siege);

        // Deselect the just-clicked button so Unity's "Selected" state
        // doesn't stack on top of our own pressed look — otherwise the
        // previously clicked tactic keeps looking pressed via the Button's
        // selectedSprite / selectedColor even after we swap the active tint.
        if (UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);

        // Update tooltip line with the concrete pros/cons of this tactic.
        if (tacticDescriptionText != null)
        {
            string key = t == CampaignTactic.Ambush   ? "MERC_TACTIC_DESC_AMBUSH"
                       : t == CampaignTactic.Siege    ? "MERC_TACTIC_DESC_SIEGE"
                                                       : "MERC_TACTIC_DESC_ASSAULT";
            tacticDescriptionText.text = LocalizationManager.Tr(key);
        }

        // Central tactic illustration always reflects the active choice —
        // fall back to the default sprite if this tactic's specific art
        // wasn't wired in the Inspector, so the slot never blanks out.
        if (tacticPreviewImage != null)
        {
            Sprite next = t == CampaignTactic.Ambush   ? tacticAmbushSprite
                       : t == CampaignTactic.Siege     ? tacticSiegeSprite
                                                        : tacticAssaultSprite;
            if (next == null) next = tacticDefaultSprite;
            if (next != null)
            {
                tacticPreviewImage.sprite = next;
                tacticPreviewImage.enabled = true;
            }
        }

        Refresh();
    }

    private void ApplyTacticPressedLook(Button btn, bool active)
    {
        if (btn == null) return;

        // SpriteSwap: swap Image.sprite to the pressed sprite when active,
        // restore the cached resting sprite when inactive. Don't touch color
        // (tint would multiply the sprite and wash it out).
        if (btn.transition == Selectable.Transition.SpriteSwap)
        {
            if (btn.targetGraphic is Image imgSw)
            {
                if (!defaultTacticSprites.ContainsKey(btn)) defaultTacticSprites[btn] = imgSw.sprite;
                var state = btn.spriteState;
                Sprite pressedSprite = state.pressedSprite != null ? state.pressedSprite : state.selectedSprite;
                imgSw.sprite = active && pressedSprite != null ? pressedSprite : defaultTacticSprites[btn];
                imgSw.color = Color.white;
            }
            return;
        }

        // ColorTint (or None): rewrite the entire ColorBlock so hovering
        // another tactic and moving off doesn't reset the active one back
        // to normal.
        Color tint = active ? tacticActiveButtonTint : tacticInactiveButtonTint;
        var cb = btn.colors;
        cb.normalColor = tint;
        cb.highlightedColor = active
            ? Color.Lerp(tint, new Color(1f, 1f, 1f, tint.a), 0.15f)
            : new Color(1f, 1f, 1f, 0.06f);
        cb.pressedColor = tint;
        cb.selectedColor = tint;
        btn.colors = cb;
        if (btn.targetGraphic is Image imgCt)
        {
            imgCt.color = tint;
        }
    }

    private void RebuildUnitRows()
    {
        var roster = MercenaryRoster.Instance;
        if (roster == null || unitRowParent == null || unitRowPrefab == null) return;

        // Guard against a mis-wired inspector reference: if the parent
        // Transform is a persistent asset (lives in a Project prefab, not in
        // the scene) Instantiate throws "Cannot instantiate objects with a
        // parent which is persistent". This usually means the panel was
        // dragged into scene but `unitRowParent` was wired to the source
        // prefab's Transform instead of the scene instance's own child.
        if (!unitRowParent.gameObject.scene.IsValid())
        {
            Debug.LogError("[PreBattlePanel] `unitRowParent` on '" + name + "' points at a PREFAB ASSET Transform, not a scene child. Open the PreBattlePanel scene instance in Hierarchy, find the row-parent GameObject inside it, and drag THAT into the field.");
            return;
        }

        // Clear old.
        for (int i = spawnedUnitRows.Count - 1; i >= 0; i--)
        {
            if (spawnedUnitRows[i] != null) Destroy(spawnedUnitRows[i]);
        }
        spawnedUnitRows.Clear();

        for (int i = 0; i < roster.catalogue.Count; i++)
        {
            var data = roster.catalogue[i];
            if (data == null) continue;
            var go = Instantiate(unitRowPrefab, unitRowParent);
            var row = go.GetComponent<PreBattleUnitRow>();
            if (row == null) row = go.AddComponent<PreBattleUnitRow>();
            row.Bind(data, roster, this, ResolvePortrait(data.unitID));
            spawnedUnitRows.Add(go);
        }
    }

    private Sprite ResolvePortrait(string unitID)
    {
        switch (unitID)
        {
            case "militia": return portraitMilitia;
            case "ranger":  return portraitRanger;
            case "knight":  return portraitKnight;
            default:        return null;
        }
    }

    private static string LocalizeRiskBand(RiskBand band)
    {
        switch (band)
        {
            case RiskBand.Overwhelming: return LocalizationManager.Tr("MERC_RISK_OVERWHELMING");
            case RiskBand.Favourable:   return LocalizationManager.Tr("MERC_RISK_FAVOURABLE");
            case RiskBand.Even:         return LocalizationManager.Tr("MERC_RISK_EVEN");
            case RiskBand.Risky:        return LocalizationManager.Tr("MERC_RISK_RISKY");
            case RiskBand.Suicidal:     return LocalizationManager.Tr("MERC_RISK_SUICIDAL");
            default: return band.ToString();
        }
    }

    // Called by a PreBattleUnitRow when its +/− buttons change.
    public void SetDesiredCount(string unitID, int count)
    {
        int idle = MercenaryRoster.Instance != null ? MercenaryRoster.Instance.CountIdle(unitID) : 0;
        desiredCounts[unitID] = Mathf.Clamp(count, 0, idle);
        Refresh();
    }

    public int GetDesiredCount(string unitID)
    {
        return desiredCounts.TryGetValue(unitID, out int v) ? v : 0;
    }

    private void Refresh()
    {
        var roster = MercenaryRoster.Instance;
        if (roster == null || currentRegion == null) return;

        // Compose the army selection into a runtime instance list for the preview.
        var chosen = new List<MercenaryUnitInstance>();
        var chosenUIDs = new List<int>();
        foreach (var kv in desiredCounts)
        {
            if (kv.Value <= 0) continue;
            var idleList = roster.GetIdleOfType(kv.Key);
            for (int i = 0; i < kv.Value && i < idleList.Count; i++)
            {
                chosen.Add(idleList[i]);
                chosenUIDs.Add(idleList[i].uid);
            }
        }

        // Travel time preview.
        if (travelTimeText != null && MercenaryCampaignManager.Instance != null)
        {
            float sec = MercenaryCampaignManager.Instance.ComputeTravelSeconds(currentRegion, currentTactic);
            travelTimeText.text = LocalizationManager.Tr("MERC_TRAVEL_TIME", FormatSeconds(sec));
        }

        // Battle forecast.
        var preview = BattleResolver.Preview(
            chosen,
            new List<MercenaryUnitData>(roster.catalogue),
            currentRegion.enemyStrength,
            currentTactic,
            currentRegion.autoBattleDiamondReward
        );

        if (armyScoreText != null) armyScoreText.text = LocalizationManager.Tr("MERC_ARMY_SCORE", preview.totalArmyScore);
        if (riskBandText != null) riskBandText.text = LocalizationManager.Tr("MERC_RISK_LABEL", LocalizeRiskBand(preview.risk));
        if (expectedCasualtiesText != null)
            expectedCasualtiesText.text = chosen.Count > 0
                ? LocalizationManager.Tr("MERC_EXPECTED_LOSSES", preview.expectedCasualtyLow, preview.expectedCasualtyHigh)
                : LocalizationManager.Tr("MERC_EXPECTED_LOSSES_NONE");

        // Win Probability — new layout uses a circular gauge + % text.
        // Formula matches BattleResolver.Resolve baseline (armyScore vs
        // enemyScore * 1.5 as the "100% win" ceiling) but clamps to 5-95
        // so the gauge never shows a fake certainty.
        float winPct = 0f;
        if (preview.totalArmyScore > 0)
        {
            // Mirror BattleResolver.Resolve exactly — 1.2× ceiling.
            float baseWin = 1f - (float)currentRegion.enemyStrength / (preview.totalArmyScore * 1.2f);
            baseWin += BattleResolver.TacticWinChanceBonus(currentTactic);
            winPct = Mathf.Clamp(baseWin, 0.05f, 0.95f);
        }
        if (chosen.Count == 0) winPct = 0f;
        if (winProbabilityText != null)
            winProbabilityText.text = chosen.Count > 0 ? $"{Mathf.RoundToInt(winPct * 100f)}%" : "—";
        if (winProbabilityFillImage != null)
            winProbabilityFillImage.fillAmount = winPct;

        // Colour both text and ring based on the win band.
        Color winColor = winPct >= 0.65f ? winColorHigh : (winPct >= 0.4f ? winColorMid : winColorLow);
        if (winProbabilityText != null) winProbabilityText.color = winColor;
        if (winProbabilityFillImage != null) winProbabilityFillImage.color = winColor;

        if (confirmButtonText != null) confirmButtonText.text = LocalizationManager.Tr("MERC_BTN_DEPLOY");
        if (confirmButton != null) confirmButton.interactable = chosen.Count > 0;

        // Update each row's live "idle available" number and its own affordability check.
        for (int i = 0; i < spawnedUnitRows.Count; i++)
        {
            var row = spawnedUnitRows[i].GetComponent<PreBattleUnitRow>();
            if (row != null) row.Refresh();
        }
    }

    private void OnConfirm()
    {
        var roster = MercenaryRoster.Instance;
        if (roster == null || currentRegion == null) return;

        var chosenUIDs = new List<int>();
        foreach (var kv in desiredCounts)
        {
            if (kv.Value <= 0) continue;
            var idleList = roster.GetIdleOfType(kv.Key);
            for (int i = 0; i < kv.Value && i < idleList.Count; i++)
                chosenUIDs.Add(idleList[i].uid);
        }

        if (chosenUIDs.Count == 0) return;
        if (MercenaryCampaignManager.Instance == null) return;

        MercenaryCampaignManager.Instance.StartCampaign(currentRegion, chosenUIDs, currentTactic);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
        Close();
    }

    private string FormatSeconds(float s)
    {
        int m = Mathf.FloorToInt(s / 60f);
        int sec = Mathf.FloorToInt(s % 60f);
        return m > 0 ? $"{m}m {sec:D2}s" : $"{sec}s";
    }
}
