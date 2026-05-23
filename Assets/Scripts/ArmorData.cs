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

    [Tooltip("Індекс цього об'єкта в масиві броні GanzSe (від 0 до 17)")]
    public int prefabIndex;

    // ДОДАНО СЛОТ ДЛЯ ІКОНКИ
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
    public float reductionPerLevel = 0.01f; // +1% захисту за рівень

    public int GetUpgradeCost(int currentLevel)
    {
        return 150 + (currentLevel * currentLevel * 100);
    }
}