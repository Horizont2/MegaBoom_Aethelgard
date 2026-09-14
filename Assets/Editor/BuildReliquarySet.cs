using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Collects the props a reliquary is built from into a Resources asset.
// See ReliquarySet for why this indirection exists.
public static class BuildReliquarySetTool
{
    private const string Path = "Assets/Resources/" + ReliquarySet.ResourceName + ".asset";

    // Paths rather than a search, because these are specific chosen props and a
    // type search would sweep up every rock in the project.
    // One chest model per grade, from the pack the project's Camp_POI already
    // uses. They carry no LootChest — the reliquary builds the interactive root
    // around them at runtime — and they share an animator whose only parameter
    // is the "Open" trigger LootChest already sends.
    private static readonly string[] ChestsByGrade =
    {
        "Assets/Animated Fantasy Polygon Chest/Prefab/Fantasy_Polygon_Chest_level_01.prefab",
        "Assets/Animated Fantasy Polygon Chest/Prefab/Fantasy_Polygon_Chest_level_02.prefab",
        "Assets/Animated Fantasy Polygon Chest/Prefab/Fantasy_Polygon_Chest_level_03.prefab",
    };

    // The pack's prefabs carry the rig but no Animator component, so the lid
    // never moved until this was attached at build time.
    private const string ChestController =
        "Assets/Animated Fantasy Polygon Chest/Animation/Fantasy_Polygon_Chest_Animation_Controller.controller";

    // What the existing chest scatters, so a reliquary drops what an ordinary
    // one does rather than inventing a second loot list to keep in step.
    private static readonly string[] Loot =
    {
        "Assets/Prefabs/Xp_Prefab.prefab",
        "Assets/Prefabs/Crystal_Prefab.prefab",
    };

    private static readonly string[] Banners =
    {
        "Assets/RPGPP_LT/Prefabs/Props/Banners/rpgpp_lt_banner_01a.prefab",
        "Assets/RPGPP_LT/Prefabs/Props/Banners/rpgpp_lt_banner_01b.prefab",
    };

    private static readonly string[] RuneStones =
    {
        "Assets/Polyart/PolyartStudio/DreamscapeMeadows/Prefabs/Rocks/Prefab_RuneRock_01.prefab",
        "Assets/Polyart/PolyartStudio/DreamscapeMeadows/Prefabs/Rocks/Prefab_RuneRock_02.prefab",
    };

    private static readonly string[] Remains =
    {
        "Assets/BTM_Assets/BTM_Items_Gems/Prefabs/Skull.prefab",
        "Assets/BTM_Assets/BTM_Items_Gems/Prefabs/SkullBones.prefab",
    };

    private const string Arch = "Assets/EmaceArt/NecroPOLY Dark Corners/Prefabs/Assets/Ruins/EA_Arch_Wall04_Ruin_01b_PRE.prefab";
    private const string Lantern = "Assets/EmaceArt/NecroPOLY Dark Corners/Prefabs/Assets/Props/EA_Exterior_Lantern_Solid_01a_PRE.prefab";

    // The region's own enemies, so a guarded site is guarded by the things that
    // live there rather than by a separate cast nobody recognises.
    // The project's existing pickups, reused so a chest's supplies land as the
    // same objects the player already picks up off the ground everywhere else.
    private const string WoodDrop = "Assets/Prefabs/Pickup/Log_Pickup.prefab";
    private const string StoneDrop = "Assets/Prefabs/Pickup/Stone_Pickup.prefab";
    private const string FoodDrop = "Assets/Prefabs/Pickup/Berry_Red.prefab";

    // The chest beacon instances and tints this. An asset reference rather than a
    // Shader.Find, so the shader is guaranteed to ship in the build.
    private const string BeamMaterial = "Assets/Materials/LightBeam_Mat.mat";

    // The project's resource icon sheet, sliced into three. Which slice is wood,
    // stone or food is not knowable from the file, so they go in sheet order and
    // the fields are swappable by hand in the inspector if the order is wrong.
    private const string ResourceIcons = "Assets/Icons/Resource Icons.png";
    // The diamond the REGION PANEL uses (Icons_Resources_3 on this sheet), so a
    // gem reads identically on a mission card and on the map. An earlier pass
    // picked UpgradePanel/Diamond.png, which is a different piece of art.
    private const string DiamondIcon = "Assets/MapUI/Icons_Resources.png";
    private const string DiamondSpriteName = "Icons_Resources_3";

    private static readonly string[] Guardians =
    {
        "Assets/Prefabs/Skeleton_Warrior.prefab",
        "Assets/Prefabs/Skeleton_Rogue.prefab",
        "Assets/Prefabs/Skeleton_Minion.prefab",
    };

    [MenuItem("Tools/Exploration/Build Reliquary Set")]
    public static void Build()
    {
        var missing = new List<string>();

        GameObject One(string p)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go == null) missing.Add(p);
            return go;
        }
        GameObject[] Many(string[] ps)
        {
            var list = new List<GameObject>(ps.Length);
            foreach (var p in ps) { var g = One(p); if (g != null) list.Add(g); }
            return list.ToArray();
        }

        Directory.CreateDirectory("Assets/Resources");
        var set = AssetDatabase.LoadAssetAtPath<ReliquarySet>(Path);
        bool isNew = set == null;
        if (isNew) set = ScriptableObject.CreateInstance<ReliquarySet>();

        set.chestByGrade = Many(ChestsByGrade);
        set.chestLoot = Many(Loot);
        set.chestAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ChestController);
        if (set.chestAnimatorController == null) missing.Add(ChestController);
        set.banners = Many(Banners);
        set.runeStones = Many(RuneStones);
        set.remains = Many(Remains);
        set.archPrefab = One(Arch);
        set.lanternPrefab = One(Lantern);
        set.guardianPrefabs = Many(Guardians);
        set.woodDrop = One(WoodDrop);
        set.stoneDrop = One(StoneDrop);
        set.foodDrop = One(FoodDrop);

        set.beamMaterial = AssetDatabase.LoadAssetAtPath<Material>(BeamMaterial);
        if (set.beamMaterial == null) missing.Add(BeamMaterial);

        // Sub-assets of a sliced sheet, in sheet order. Only overwritten when the
        // sheet resolves, so a hand-corrected assignment survives a rebuild.
        var icons = new List<Sprite>(3);
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(ResourceIcons))
            if (sub is Sprite sp) icons.Add(sp);
        icons.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        if (icons.Count >= 3)
        {
            set.woodIcon = icons[0];
            set.stoneIcon = icons[1];
            set.foodIcon = icons[2];
        }
        else missing.Add(ResourceIcons + " (needs 3 sliced sprites)");

        // Diamond.png is a MULTI-sprite texture, so LoadAssetAtPath<Sprite>
        // returns null on it — the sprites are sub-assets. That is why wiring it
        // that way would have quietly assigned nothing.
        Sprite gem = null;
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(DiamondIcon))
            if (sub is Sprite sp && sp.name == DiamondSpriteName) { gem = sp; break; }
        if (gem != null) set.diamondIcon = gem;
        else missing.Add(DiamondIcon + " (no sprite sub-asset — is it sliced?)");

        if (isNew) AssetDatabase.CreateAsset(set, Path);
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        ReliquarySet.ClearCache();

        // The chest is the only thing that is fatal — everything else degrades
        // into a plainer shrine, which is worth saying rather than failing over.
        if (set.ChestFor(0) == null)
        {
            Debug.LogError("[Reliquary] No chest model resolved. Without one a reliquary is decoration with " +
                           "nothing to open, and the director will refuse to place any.");
        }
        else if (set.chestByGrade.Length < 3)
        {
            Debug.LogWarning($"[Reliquary] Only {set.chestByGrade.Length} of 3 chest grades resolved — the higher " +
                             "grades will fall back to a lower chest, so the site's grade will not read from the " +
                             "chest itself.");
        }

        if (set.woodDrop == null || set.stoneDrop == null || set.foodDrop == null)
            Debug.LogWarning("[Reliquary] A supply pickup prefab is missing. Those resources will be credited " +
                             "straight to the backpack instead of bursting out of the chest — correct, but invisible, " +
                             "which is the exact thing the visible payout exists to fix.");

        if (set.banners.Length == 0)
            Debug.LogWarning("[Reliquary] No banner prefabs resolved. Banners ARE the landmark — without them the " +
                             "site is invisible until the player is standing on it, which defeats the whole feature.");

        string report = $"[Reliquary] Set built -> {Path}\n" +
                        $"  chests {set.chestByGrade.Length}/3, loot {set.chestLoot.Length}, " +
                        $"banners {set.banners.Length}, stones {set.runeStones.Length}, remains {set.remains.Length}, " +
                        $"arch {(set.archPrefab != null ? "yes" : "no")}, lantern {(set.lanternPrefab != null ? "yes" : "no")}";
        if (missing.Count > 0) Debug.LogWarning(report + "\n  Not found:\n    " + string.Join("\n    ", missing));
        else Debug.Log(report);

        Selection.activeObject = set;
    }
}
