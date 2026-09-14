using UnityEngine;

// The prefabs a reliquary is built from, reachable at runtime.
//
// Same reason as WeaponIndex and EnemyAnimationSet: these live all over
// Assets/, AssetDatabase does not exist in a build, and a component created from
// nothing at runtime has no inspector for anyone to wire. An editor tool collects
// them here once and the game loads one asset.
[CreateAssetMenu(fileName = "ReliquarySet", menuName = "Exploration/Reliquary Set")]
public class ReliquarySet : ScriptableObject
{
    public const string ResourceName = "ReliquarySet";

    [Tooltip("Model only, one per grade: level_01 wayside, level_02 shrine, level_03 barrow. No LootChest on these — the reliquary builds the interactive root around them, so the chest the player sees says which grade it is before they are close enough to read anything else.")]
    public GameObject[] chestByGrade = new GameObject[3];

    [Tooltip("What a chest scatters when it opens. Read off the project's existing chest so a reliquary drops exactly what an ordinary one does.")]
    public GameObject[] chestLoot;

    [Tooltip("The chest pack's animator. Its prefabs carry the rig but no Animator component, so without this the lid never moves.")]
    public RuntimeAnimatorController chestAnimatorController;

    [Tooltip("The landmark. Tall, coloured, and the one prop that reads across broken terrain.")]
    public GameObject[] banners;

    [Tooltip("Ring stones — what makes it legible as a shrine rather than as scenery.")]
    public GameObject[] runeStones;

    [Tooltip("Bones and skulls, legendary sites only.")]
    public GameObject[] remains;

    public GameObject archPrefab;
    public GameObject lanternPrefab;

    [Tooltip("Skeletons posted at the site. They stand dormant until the player is close — see Reliquary.PostGuardians.")]
    public GameObject[] guardianPrefabs;

    [Header("Built site prefabs — one per grade")]
    [Tooltip("Assets/Prefabs/Exploration/Reliquary_Wayside|Shrine|Barrow. The whole interactive site, authored: chest model at the right SIZE, animator attached, LootChest and Reliquary wired. Drop one into a hand-built location and that location becomes a reliquary. Built by Tools > Exploration > Build Reliquary Prefabs.")]
    public GameObject[] sitePrefabByGrade = new GameObject[3];

    [Header("Beacon")]
    [Tooltip("Transparent material the chest's light shaft instances and tints. Passed in rather than found at runtime because Shader.Find on a URP package shader is not dependable in a build — an asset reference guarantees the shader ships.")]
    public Material beamMaterial;

    [Header("Reward reveal icons")]
    [Tooltip("Shown centre-screen when a chest pays out supplies, the same way a piece of armour is revealed. Sliced out of the project's resource icon sheet; if the three come out in the wrong order, just swap them here.")]
    public Sprite woodIcon;
    public Sprite stoneIcon;
    public Sprite foodIcon;
    [Tooltip("Diamonds/gems. Assigned by Tools > Exploration > Build Reliquary Set from Assets/UpgradePanel/Diamond.png, the same art the crystal node uses.")]
    public Sprite diamondIcon;

    [Header("Visible payout")]
    [Tooltip("Physical pickups thrown out of the chest as the lid opens, so the reward is something the player watches land and collects — not a number that changes on the HUD.")]
    public GameObject woodDrop;
    public GameObject stoneDrop;
    public GameObject foodDrop;

    // Falls back down the grades the same way ChestFor does, so a set built
    // before the prefabs existed still yields something instead of nothing.
    public GameObject SiteFor(int grade)
    {
        if (sitePrefabByGrade == null || sitePrefabByGrade.Length == 0) return null;
        for (int i = Mathf.Clamp(grade, 0, sitePrefabByGrade.Length - 1); i >= 0; i--)
            if (sitePrefabByGrade[i] != null) return sitePrefabByGrade[i];
        foreach (var g in sitePrefabByGrade) if (g != null) return g;
        return null;
    }

    private static ReliquarySet _cached;
    private static bool _searched;

    public static ReliquarySet Load()
    {
        if (_searched) return _cached;
        _searched = true;
        _cached = Resources.Load<ReliquarySet>(ResourceName);
        if (_cached == null)
            Debug.LogWarning($"[Reliquary] No '{ResourceName}' in a Resources folder — no reliquaries will be placed. " +
                             "Build it with Tools > Exploration > Build Reliquary Set.");
        return _cached;
    }

    public static void ClearCache() { _cached = null; _searched = false; }

    public bool IsUsable => ChestFor(0) != null;

    // Falls back down the grades rather than returning nothing: a barrow with a
    // level_01 chest still works, a barrow with no chest is decoration.
    public GameObject ChestFor(int grade)
    {
        if (chestByGrade == null || chestByGrade.Length == 0) return null;
        for (int i = Mathf.Clamp(grade, 0, chestByGrade.Length - 1); i >= 0; i--)
            if (chestByGrade[i] != null) return chestByGrade[i];
        foreach (var g in chestByGrade) if (g != null) return g;
        return null;
    }

    public GameObject PickBanner() => Pick(banners);
    public GameObject PickRuneStone() => Pick(runeStones);
    public GameObject PickRemains() => Pick(remains);

    private static GameObject Pick(GameObject[] pool)
    {
        if (pool == null || pool.Length == 0) return null;
        for (int i = 0; i < 4; i++)
        {
            var g = pool[Random.Range(0, pool.Length)];
            if (g != null) return g;
        }
        foreach (var g in pool) if (g != null) return g;
        return null;
    }
}
