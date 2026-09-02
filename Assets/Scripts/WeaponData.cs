using UnityEngine;

public enum ItemCategory { Sword, Axe, Bow, Helmet, Armor, Gloves } // ����: �������

[CreateAssetMenu(fileName = "NewWeapon", menuName = "Game/Weapon Data")]
public class WeaponData : ScriptableObject
{
    [Header("Basic Info")]
    public int weaponID;
    public string weaponName;
    public ItemCategory category; // ����: ���� ������� � ���������

    public Sprite icon;

    [TextArea(3, 5)]
    public string description;
    public int price;

    [Header("Models")]
    public GameObject shopPrefab;
    public GameObject inGamePrefab;
    [Tooltip("Extra local rotation (Euler °) for the shop-preview weapon. Leave 0 for prefabs authored upright; use it to fix a prefab whose mesh sits crooked at identity (e.g. the default sword).")]
    public Vector3 shopRotationEuler = Vector3.zero;

    [Header("Power System")]
    public int basePower = 20;
    public int powerPerLevel = 15;

    [Header("Upgrade System")]
    public int maxUpgradeLevel = 5;
    public int baseUpgradeCost = 100;
    public float upgradeCostMultiplier = 1.5f;

    [Header("Base Stats")]
    public float damageBonus;
    public float attackSpeed;
    public float critChance;

    [Header("Stat Growth Per Level")]
    public float damagePerLevel = 10f;
    public float attackSpeedPerLevel = 0.02f;
    public float critChancePerLevel = 0.02f;

    public int GetUpgradeCost(int currentLevel)
    {
        return Mathf.RoundToInt(baseUpgradeCost * Mathf.Pow(upgradeCostMultiplier, currentLevel));
    }
}