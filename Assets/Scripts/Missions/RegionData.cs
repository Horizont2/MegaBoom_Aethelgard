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

    // --- НОВИЙ БЛОК: СИСТЕМА БОСІВ ТА КВЕСТІВ ---
    [Header("Region Objective (Totem & Bosses)")]
    [Tooltip("Префаб Тотему/Обеліска, який гравець має знайти на локації")]
    public GameObject regionTotemPrefab;
    [Tooltip("Список босів, які заспавняться під час активації Тотему")]
    public GameObject[] regionBossPrefabs;

    [Header("One-Time Rewards (За проходження)")]
    public int woodReward = 100;
    public int stoneReward = 50;
    public int foodReward = 20;
    public int diamondReward = 5;

    [Header("Upgrade System (5 Levels)")]
    [Tooltip("Заповніть 5 елементів. Element 0 = Level 1 (Безкоштовно)")]
    public RegionLevelData[] upgradeLevels = new RegionLevelData[5];
}