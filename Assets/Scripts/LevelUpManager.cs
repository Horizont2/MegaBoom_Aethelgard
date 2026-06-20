using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public enum UpgradeType { Health, Speed, Damage, PickupRadius, AttackSpeed, Armor, HealthRegen }

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
            case UpgradeType.Armor: if (player != null) player.damageReduction += upgrade.amount; break;
            case UpgradeType.HealthRegen: if (player != null) player.healthRegenRate += upgrade.amount; break;
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