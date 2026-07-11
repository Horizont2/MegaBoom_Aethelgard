using UnityEngine;
using System.Collections.Generic;

public enum RegionState { Locked, Available, Conquered }
public enum RegionBiome { Forest, Desert, Winter }

[System.Serializable]
public class RegionLevelData
{
    [Header("Upgrade Cost")]
    public int costWood;
    public int costStone;
    public int costFood;

    [Header("Passive Income (Per Hour)")]
    public int passiveWood;
    public int passiveStone;
    public int passiveFood;
    public int passiveDiamonds;
}

[CreateAssetMenu(fileName = "NewRegion", menuName = "Map/Region Data")]
public class RegionData : ScriptableObject
{
    [Header("Lore & UI")]
    public int regionID;
    public string regionName = "New Territory";
    [TextArea(3, 5)] public string loreDescription;
    public Sprite regionIllustration;

    [Header("Generation Settings")]
    public RegionBiome regionBiome = RegionBiome.Forest;

    [Header("Map Logic")]
    public RegionState currentState = RegionState.Locked;
    public List<RegionData> neighboringRegions;
    [HideInInspector] public bool isNewlyUnlocked = false;

    [Header("Difficulty & Combat System")]
    public int recommendedPower = 150;
    public float enemyHpMultiplier = 1f;
    public float enemyDamageMultiplier = 1f;

    [Header("Mercenary Auto-Battle (regions without a totem)")]
    [Tooltip("Гарнізонна сила регіону — база для розрахунку авто-битви найманців. Використовується тільки коли regionTotemPrefab == null (немає локації для гравця).")]
    public int enemyStrength = 200;
    [Tooltip("Скільки діамантів гравець отримає за перемогу авто-битви найманців.")]
    public int autoBattleDiamondReward = 15;

    // --- ����� ����: ������� ��Ѳ� �� ����Ҳ� ---
    [Header("Region Objective (Totem & Bosses)")]
    [Tooltip("������ ������/�������, ���� ������� �� ������ �� �������")]
    public GameObject regionTotemPrefab;
    [Tooltip("Vertical nudge (metres) applied to this region's location prefab after it is placed on the ground. Use it to fix prefabs whose pivot is not at their base — positive lifts up, negative sinks down. Leave 0 if the prefab pivot is authored at ground level.")]
    public float locationYOffset = 0f;
    [Tooltip("������ ����, �� ������������ �� ��� ��������� ������")]
    public GameObject[] regionBossPrefabs;

    [Header("One-Time Rewards (�� �����������)")]
    public int woodReward = 100;
    public int stoneReward = 50;
    public int foodReward = 20;
    public int diamondReward = 5;

    [Header("Upgrade System (5 Levels)")]
    [Tooltip("��������� 5 ��������. Element 0 = Level 1 (�����������)")]
    public RegionLevelData[] upgradeLevels = new RegionLevelData[5];
}