using UnityEngine;

public enum ArmorCategory
{
    Head, Chest, Arms, Belt, Legs, Feet
}

[CreateAssetMenu(fileName = "New Armor Data", menuName = "Game Data/Armor Data")]
public class ArmorData : ScriptableObject
{
    [Header("Main Info")]
    public int armorID;
    public string armorName;
    [TextArea(2, 4)]
    public string description;
    public ArmorCategory category;

    [Tooltip("������ ����� ��'���� � ����� ����� GanzSe (�� 0 �� 17)")]
    public int prefabIndex;

    // ������ ���� ��� ������
    public Sprite icon;

    [Header("Economy")]
    public int price;
    public int maxUpgradeLevel = 5;

    [Header("Base Stats")]
    public int basePower = 10;
    public float baseHealthBonus = 0f;
    public float baseDamageReduction = 0f;

    [Header("Upgrade Scaling")]
    public int powerPerLevel = 5;
    public float healthPerLevel = 5f;
    public float reductionPerLevel = 0.01f; // +1% ������� �� �����

    public int GetUpgradeCost(int currentLevel)
    {
        // Linear ramp: cheap first upgrade, predictable progression.
        // Total 0→5: 80+140+200+260+320 = 1000 diamonds per piece.
        // Old formula `150 + lvl*lvl*100` totalled 3750 per piece — that was
        // 22.5k for a full set, on top of the buy price.
        return 80 + (currentLevel * 60);
    }
}