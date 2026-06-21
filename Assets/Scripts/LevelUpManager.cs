using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public enum UpgradeType
{
    Health, Speed, Damage, PickupRadius, AttackSpeed, Armor, HealthRegen,
    // === Extended upgrades (LvlUp polish pass) ===
    CritChance,
    CritDamage,
    LifeSteal,
    DodgeChance,
    KillHeal,
    ThornDamage,
    XPGainBonus,
    DiamondBonus,
}

[System.Serializable]
public class UpgradeData
{
    public string upgradeName;
    [TextArea(2, 3)] public string description;
    public string statDisplay;
    public Sprite icon;
    public UpgradeType type;
    public float amount;
}

public class LevelUpManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject levelUpPanel;
    public UIStarEffect starEffect;
    public UpgradeButtonUI[] uiButtons;
    public Button randomButton;

    [Header("Database")]
    public List<UpgradeData> allPossibleUpgrades;

    [Header("AAA VFX")]
    [Tooltip("�����, ���� ���������� �� ������� ���� ������ ��������")]
    public GameObject playerUpgradeVFXPrefab;

    private PlayerController player;
    private HammerDamage hammer;
    private WeaponOrbit weaponOrbit;

    private UpgradeData[] currentOptions = new UpgradeData[3];

    private void Start()
    {
        player = FindFirstObjectByType<PlayerController>();
        hammer = FindFirstObjectByType<HammerDamage>();
        weaponOrbit = FindFirstObjectByType<WeaponOrbit>();
        levelUpPanel.SetActive(false);

        if (randomButton != null) randomButton.onClick.AddListener(OnRandomClicked);

        EnsureFullUpgradeCatalog();
    }

    // Populates allPossibleUpgrades at runtime so every UpgradeType has at least
    // one entry — keeps the LvlUp menu varied without forcing a scene re-wire each
    // time a new upgrade type is added.
    private void EnsureFullUpgradeCatalog()
    {
        if (allPossibleUpgrades == null) allPossibleUpgrades = new List<UpgradeData>();

        System.Collections.Generic.HashSet<UpgradeType> present = new System.Collections.Generic.HashSet<UpgradeType>();
        foreach (UpgradeData u in allPossibleUpgrades) if (u != null) present.Add(u.type);

        Sprite fallbackIcon = null;
        for (int i = 0; i < allPossibleUpgrades.Count; i++)
        {
            if (allPossibleUpgrades[i] != null && allPossibleUpgrades[i].icon != null)
            { fallbackIcon = allPossibleUpgrades[i].icon; break; }
        }

        AddIfMissing(present, fallbackIcon, UpgradeType.Health,        "Vitality Reserves",  "Forged sinew. Each layer means another swing you outlast.",            "+10 Max HP",         10f);
        AddIfMissing(present, fallbackIcon, UpgradeType.Speed,         "Vanguard March",     "Lighter step, longer stride. The blade always arrives first.",         "+0.5 Speed",         0.5f);
        AddIfMissing(present, fallbackIcon, UpgradeType.Damage,        "Siege Might",        "The hammer drinks deeper. Bones break at half the effort.",            "+5 Damage",          5f);
        AddIfMissing(present, fallbackIcon, UpgradeType.PickupRadius,  "Crystal Lure",       "Aether shards leap toward you from farther afield.",                   "+0.5 Pickup Range",  0.5f);
        AddIfMissing(present, fallbackIcon, UpgradeType.AttackSpeed,   "Whetstone Rhythm",   "The swing-arc tightens. More strikes per breath.",                     "+15 Atk Speed",      15f);
        AddIfMissing(present, fallbackIcon, UpgradeType.Armor,         "Aethelgard Plate",   "Damp the next blow with old steel and older oaths.",                   "+5% Damage Resist",  0.05f);
        AddIfMissing(present, fallbackIcon, UpgradeType.HealthRegen,   "Field Medicine",     "Slow knit, but knit it does. Health returns with every footfall.",     "+0.3 HP/sec",        0.3f);
        AddIfMissing(present, fallbackIcon, UpgradeType.CritChance,    "Keen Eye",           "You read where bone is brittle. Strikes find the weak point oftener.","+5% Crit Chance",    0.05f);
        AddIfMissing(present, fallbackIcon, UpgradeType.CritDamage,    "Executioner's Edge", "When the blade bites true, it bites deeper.",                          "+25% Crit Damage",   0.25f);
        AddIfMissing(present, fallbackIcon, UpgradeType.LifeSteal,     "Bloodbound Pact",    "Every wound you deliver feeds you back a sip.",                        "+5% Lifesteal",      0.05f);
        AddIfMissing(present, fallbackIcon, UpgradeType.DodgeChance,   "Wind-Touched",       "The air parts before you. Some blows pass through nothing.",           "+5% Dodge",          0.05f);
        AddIfMissing(present, fallbackIcon, UpgradeType.KillHeal,      "Reaver's Reward",    "Each kill stitches another scar shut.",                                "+3 HP per Kill",     3f);
        AddIfMissing(present, fallbackIcon, UpgradeType.ThornDamage,   "Wardbreaker Sigil",  "Those who strike you bleed for the privilege.",                        "+15% Thorns",        0.15f);
        AddIfMissing(present, fallbackIcon, UpgradeType.XPGainBonus,   "Soulreader",         "You hear the song each fallen soul carries. Learn faster.",            "+15% XP Gain",       0.15f);
        AddIfMissing(present, fallbackIcon, UpgradeType.DiamondBonus,  "Hoarder's Gaze",     "Aether shards spill heavier where you walk.",                          "+20% Diamond Gain",  0.20f);
    }

    private void AddIfMissing(System.Collections.Generic.HashSet<UpgradeType> present, Sprite fallbackIcon, UpgradeType type, string name, string desc, string stat, float amount)
    {
        if (present.Contains(type)) return;
        allPossibleUpgrades.Add(new UpgradeData
        {
            upgradeName = name,
            description = desc,
            statDisplay = stat,
            icon = fallbackIcon,
            type = type,
            amount = amount,
        });
    }

    public void ShowMenu()
    {
        levelUpPanel.SetActive(true);
        Time.timeScale = 0f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (starEffect != null) starEffect.PlayEffect();
        if (randomButton != null) randomButton.interactable = true;

        GenerateRandomChoices();

        if (TutorialHints.Instance != null)
            TutorialHints.Instance.ShowIfNew("LevelUp",
                "Each level lets you pick one of three upgrades. Hover a card to read its effect, click to commit.", 6f);
    }

    private void GenerateRandomChoices()
    {
        List<UpgradeData> availablePool = new List<UpgradeData>(allPossibleUpgrades);

        for (int i = 0; i < uiButtons.Length; i++)
        {
            uiButtons[i].ResetVisuals();
            uiButtons[i].ResetTextColors();
            uiButtons[i].buttonComponent.interactable = true;

            if (availablePool.Count == 0) break;

            int randomIndex = Random.Range(0, availablePool.Count);
            UpgradeData chosenUpgrade = availablePool[randomIndex];
            currentOptions[i] = chosenUpgrade;

            uiButtons[i].titleText.text = chosenUpgrade.upgradeName;

            string finalDescription = chosenUpgrade.description;
            if (!string.IsNullOrEmpty(chosenUpgrade.statDisplay))
            {
                finalDescription += "\n<color=#FFD700><b>" + chosenUpgrade.statDisplay + "</b></color>";
            }
            uiButtons[i].descriptionText.text = finalDescription;

            if (chosenUpgrade.icon != null) uiButtons[i].iconImage.sprite = chosenUpgrade.icon;
            if (uiButtons[i].statText != null) uiButtons[i].statText.gameObject.SetActive(false);

            UpgradeData upgradeToApply = chosenUpgrade;
            int buttonIndex = i;

            uiButtons[i].buttonComponent.onClick.RemoveAllListeners();
            uiButtons[i].buttonComponent.onClick.AddListener(() => OnStandardUpgradeClicked(upgradeToApply, buttonIndex));

            availablePool.RemoveAt(randomIndex);
        }
    }

    private void OnStandardUpgradeClicked(UpgradeData upgrade, int buttonIndex)
    {
        BlockAllButtons();
        uiButtons[buttonIndex].HighlightAsSelected();
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);

        StartCoroutine(ApplyWithDelayRoutine(upgrade));
    }

    private void OnRandomClicked()
    {
        BlockAllButtons();
        StartCoroutine(RouletteRoutine());
    }

    private void BlockAllButtons()
    {
        if (randomButton != null) randomButton.interactable = false;
        foreach (var btn in uiButtons) btn.buttonComponent.interactable = false;
    }

    private IEnumerator RouletteRoutine()
    {
        int jumps = Random.Range(10, 16);
        int currentIndex = 0;
        float delay = 0.05f;

        for (int i = 0; i < jumps; i++)
        {
            foreach (var btn in uiButtons) btn.ResetVisuals();
            currentIndex = i % uiButtons.Length;
            uiButtons[currentIndex].bgImage.color = uiButtons[currentIndex].hoverColor;

            if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Hover);

            yield return new WaitForSecondsRealtime(delay);
            delay += 0.015f;
        }

        UpgradeData chosenUpgrade = currentOptions[currentIndex];
        foreach (var btn in uiButtons) btn.ResetVisuals();
        uiButtons[currentIndex].HighlightAsSelected();

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_LevelUp);
        yield return new WaitForSecondsRealtime(0.8f);

        ApplyUpgrade(chosenUpgrade);
    }

    private IEnumerator ApplyWithDelayRoutine(UpgradeData upgrade)
    {
        yield return new WaitForSecondsRealtime(0.4f);
        ApplyUpgrade(upgrade);
    }

    public void ApplyUpgrade(UpgradeData upgrade)
    {
        switch (upgrade.type)
        {
            case UpgradeType.Health:
                player.maxHealth += upgrade.amount;
                player.currentHealth += upgrade.amount;
                break;
            case UpgradeType.Speed: player.moveSpeed += upgrade.amount; break;
            case UpgradeType.Damage:
                if (player != null) player.globalDamageMultiplier += (upgrade.amount / 100f);
                if (hammer != null) hammer.baseDamage += upgrade.amount;
                break;
            case UpgradeType.PickupRadius: if (player != null) player.pickupRadius += upgrade.amount; break;
            case UpgradeType.AttackSpeed: if (weaponOrbit != null) weaponOrbit.baseRotationSpeed += upgrade.amount; break;
            case UpgradeType.Armor:
                if (player != null) player.damageReduction = Mathf.Clamp(player.damageReduction + upgrade.amount, 0f, 0.85f);
                break;
            case UpgradeType.HealthRegen: if (player != null) player.healthRegenRate += upgrade.amount; break;

            case UpgradeType.CritChance:
                if (player != null) player.globalCritChance = Mathf.Clamp(player.globalCritChance + upgrade.amount, 0f, 1f);
                break;
            case UpgradeType.CritDamage:
                if (player != null) player.critDamageMultiplier += upgrade.amount;
                break;
            case UpgradeType.LifeSteal:
                if (player != null) player.lifeStealFraction = Mathf.Clamp(player.lifeStealFraction + upgrade.amount, 0f, 1f);
                break;
            case UpgradeType.DodgeChance:
                if (player != null) player.dodgeChance = Mathf.Clamp(player.dodgeChance + upgrade.amount, 0f, 0.75f);
                break;
            case UpgradeType.KillHeal:
                if (player != null) player.killHealAmount += upgrade.amount;
                break;
            case UpgradeType.ThornDamage:
                if (player != null) player.thornDamageFraction = Mathf.Clamp(player.thornDamageFraction + upgrade.amount, 0f, 2f);
                break;
            case UpgradeType.XPGainBonus:
                if (player != null) player.xpGainMultiplier += upgrade.amount;
                break;
            case UpgradeType.DiamondBonus:
                if (player != null) player.diamondBonusMultiplier += upgrade.amount;
                break;
        }

        // --- ²��������� ������ �������� (� ������� ���������) ---
        if (playerUpgradeVFXPrefab != null && player != null)
        {
            GameObject vfx = Instantiate(playerUpgradeVFXPrefab, player.transform.position, Quaternion.identity);
            vfx.transform.SetParent(player.transform);

            // ��������� �������� ��� ��������� �������� (���������, ����� 䳺 2 �������)
            StartCoroutine(FadeOutVFX(vfx, 2f));
        }

        if (player != null) player.UpdateHUD();
        ResumeGame();
    }

    // ����� �����: ������ ������� Particle System ������ ��������� ���������
    private IEnumerator FadeOutVFX(GameObject vfx, float activeTime)
    {
        // 1. ������, ���� ����� "������"
        yield return new WaitForSeconds(activeTime);

        if (vfx != null)
        {
            // 2. ��������� �� ������� �������� ��������� ������� � ��������� �� �'���
            ParticleSystem[] pSystems = vfx.GetComponentsInChildren<ParticleSystem>();
            foreach (var ps in pSystems)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            // 3. ������ �� 3 �������, ��� ���� ����� ������� ��������� � �����
            yield return new WaitForSeconds(3f);

            // 4. ҳ���� ����� ��������� "������" ��'���
            if (vfx != null) Destroy(vfx);
        }
    }

    private void ResumeGame()
    {
        levelUpPanel.SetActive(false);
        Time.timeScale = 1f;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}