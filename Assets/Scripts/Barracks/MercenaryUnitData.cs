using UnityEngine;

// One entry per mercenary archetype (Militia, Ranger, Knight). Held as an
// asset in Assets/RegionData/Mercenaries so designers can retune stats
// without touching code. Runtime instances live in MercenaryRoster.
[CreateAssetMenu(fileName = "NewMercUnit", menuName = "Camp/Mercenary Unit Data")]
public class MercenaryUnitData : ScriptableObject
{
    [Header("Identity")]
    public string unitID = "militia";
    public string displayName = "Militia";
    [TextArea(2, 4)] public string flavourText;

    [Header("UI Art")]
    public Sprite icon;
    public Sprite portrait;
    // Small marker for the world-map figurine when the army is walking.
    public Sprite mapFigurineSprite;

    [Header("Camp Presence")]
    // Model dropped near the barracks — Idle+Walk animator only, no combat.
    public GameObject campPrefab;

    [Header("Economy")]
    public int baseHireCost = 100;

    [Header("Combat (Auto-Battle)")]
    // Both scale linearly with unit level: baseAttack + perLevel * (level-1).
    public int baseAttack = 20;
    public int attackPerLevel = 8;
    public int baseHP = 60;
    public int hpPerLevel = 25;

    [Header("Upgrade")]
    // Gold cost of upgrading THIS archetype from level N to N+1. Element 0
    // = price to go from 1→2. Length defines the max reachable level.
    public int[] upgradePricePerLevel = new int[] { 200, 500, 1000, 2000 };

    [Header("Barracks Requirement")]
    // Minimum barracks building level required to hire this archetype.
    public int minBarracksLevel = 1;

    public int MaxLevel => (upgradePricePerLevel != null ? upgradePricePerLevel.Length : 0) + 1;

    public int AttackAtLevel(int level) => baseAttack + attackPerLevel * Mathf.Max(0, level - 1);
    public int HPAtLevel(int level) => baseHP + hpPerLevel * Mathf.Max(0, level - 1);
    // A single "score" used by BattleResolver — cheap composite of atk*hp so
    // upgrades scale both dimensions without me having to model an ATK/DEF loop.
    public int ScoreAtLevel(int level) => Mathf.CeilToInt(AttackAtLevel(level) * (HPAtLevel(level) / 20f));
}
