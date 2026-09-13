using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Authors the shields, and teaches the player's animator how to hold one.
//
// Two jobs in one menu because they are one job in practice: a shield you can
// buy but cannot visibly raise is half a feature, and an animator that knows
// about blocking with nothing to block with is the other half.
public static class BuildShieldSetTool
{
    private const string ShieldFolder = "Assets/ShopItems/Shields";
    private const string ModelFolder = "Assets/MainCharacters/Assets/fbx(unity)";
    private const string HeroAnimator = "Assets/MainCharacters/Animations/fbx/HeroAnimator.controller";

    // The DoubleL pack ships exactly the two clips this needs: a hold pose and
    // a flinch. Nothing else in the project has them.
    private const string BlockIdleClip = "Assets/DoubleL/Demo/Anim/OneHand_Up_Shield_Block_Idle.anim";
    private const string BlockHitClip = "Assets/DoubleL/Demo/Anim/OneHand_Up_Shield_Block_Hit_1.anim";

    // Four bargains, not four rungs. See WeaponData's shield block for why.
    private struct Spec
    {
        public string model, name, description;
        public int price;
        public float parry, stamina, angle, reflect, pool;
    }

    private static readonly Spec[] Specs =
    {
        new Spec { model = "shield_round", name = "Wayfarer's Round", price = 0,
                   description = "Light limewood. Quick to bring up and cheap to hold, but it covers little.",
                   parry = 0.06f, stamina = 0.8f, angle = -30f, reflect = 0f, pool = 0f },

        new Spec { model = "shield_square", name = "Warden's Kite", price = 420,
                   description = "The soldier's answer. No strength anywhere, no weakness either.",
                   parry = 0f, stamina = 1f, angle = 0f, reflect = 0f, pool = 10f },

        new Spec { model = "shield_spikes", name = "Spiked Bulwark", price = 780,
                   description = "Studded with iron. Every blow you catch bites the hand that threw it.",
                   parry = -0.04f, stamina = 1.1f, angle = 10f, reflect = 0.25f, pool = 0f },

        new Spec { model = "shield_round_barbarian", name = "Ironhide Slab", price = 1150,
                   description = "A wall with a strap. Nearly impossible to parry with, nearly impossible to flank.",
                   parry = -0.06f, stamina = 1.4f, angle = 25f, reflect = 0f, pool = 40f },
    };

    [MenuItem("Tools/Combat/Build Shield Set")]
    public static void Build()
    {
        Directory.CreateDirectory(ShieldFolder);

        // IDs start high and well clear of the weapons. Shields share
        // WeaponData and therefore share the ID space, and a collision would
        // silently equip a sword as a shield.
        int nextId = 900;
        var made = new List<WeaponData>();
        var missing = new List<string>();

        foreach (var s in Specs)
        {
            string modelPath = $"{ModelFolder}/{s.model}.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) { missing.Add(modelPath); continue; }

            string path = $"{ShieldFolder}/{s.model}.asset";
            var data = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
            bool isNew = data == null;
            if (isNew) data = ScriptableObject.CreateInstance<WeaponData>();

            // The ID is only assigned on creation. Rewriting it on a rebuild
            // would orphan every save that already owns this shield.
            if (isNew) data.weaponID = nextId;
            nextId++;

            data.weaponName = s.name;
            data.description = s.description;
            data.category = ItemCategory.Shield;
            data.price = s.price;
            data.shopPrefab = model;
            data.inGamePrefab = model;
            data.maxUpgradeLevel = 5;

            data.parryWindowBonus = s.parry;
            data.staminaMultiplier = s.stamina;
            data.guardAngleBonus = s.angle;
            data.reflectFraction = s.reflect;
            data.bonusStamina = s.pool;

            // A shield's power rating exists so the shop can sort and the
            // region gate can count it; it is not what makes one good.
            data.basePower = 8 + Mathf.RoundToInt(s.price / 100f);
            data.powerPerLevel = 4;

            if (isNew) AssetDatabase.CreateAsset(data, path);
            EditorUtility.SetDirty(data);
            made.Add(data);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // The free one is owned from the start, or a new player has a block
        // button and nothing to block with.
        if (made.Count > 0 && made[0].price == 0)
        {
            PlayerPrefs.SetInt(ShieldLoadout.PP_UNLOCK_PREFIX + made[0].weaponID, 1);
            if (!PlayerPrefs.HasKey(ShieldLoadout.PP_SELECTED))
                PlayerPrefs.SetInt(ShieldLoadout.PP_SELECTED, made[0].weaponID);
            PlayerPrefs.Save();
        }

        string report = $"[Shields] Built {made.Count} shields in {ShieldFolder}.";
        if (missing.Count > 0) report += "\n  Models not found:\n    " + string.Join("\n    ", missing);
        report += "\n\nNow run Tools > Shop > Build Weapon Index so they are reachable at runtime, " +
                  "and add them to ShopManager.weapons for the shop list.";
        Debug.Log(report);
    }

    // ---------------------------------------------------------------------

    // Adds the guard to the player's animator on a MASKED OVERRIDE LAYER.
    //
    // Not by editing the base layer, and that is the important part. The base
    // layer is where running, attacking and dying live; adding states to it
    // means new transitions to every one of them and a real chance of breaking
    // locomotion — and this project has already lost its enemy animations once
    // to a careless animator edit. A separate additive layer with its own
    // states cannot affect anything that already works: at weight 0 it does not
    // exist, and the base layer is untouched either way.
    [MenuItem("Tools/Combat/Add Block To Player Animator")]
    public static void AddBlockLayer()
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(HeroAnimator);
        if (ctrl == null)
        {
            Debug.LogError($"[Shields] No animator at {HeroAnimator}. If the player's controller moved, point this " +
                           "tool at the new path.");
            return;
        }

        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(BlockIdleClip);
        var hit = AssetDatabase.LoadAssetAtPath<AnimationClip>(BlockHitClip);
        if (idle == null)
        {
            Debug.LogError($"[Shields] No block pose at {BlockIdleClip}. Without a clip there is nothing to add.");
            return;
        }

        if (!HasParameter(ctrl, "isBlocking")) ctrl.AddParameter("isBlocking", AnimatorControllerParameterType.Bool);
        if (!HasParameter(ctrl, "BlockHit")) ctrl.AddParameter("BlockHit", AnimatorControllerParameterType.Trigger);

        foreach (var existing in ctrl.layers)
        {
            if (existing.name != "Block") continue;
            Debug.Log("[Shields] The Block layer is already on the player's animator — parameters refreshed, " +
                      "layer left alone so any hand-tuning survives.");
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            return;
        }

        var layer = new AnimatorControllerLayer
        {
            name = "Block",
            defaultWeight = 1f,
            blendingMode = AnimatorLayerBlendingMode.Override,
            stateMachine = new AnimatorStateMachine { name = "Block", hideFlags = HideFlags.HideInHierarchy },
        };
        AssetDatabase.AddObjectToAsset(layer.stateMachine, ctrl);

        var sm = layer.stateMachine;
        // An EMPTY default state is what keeps the layer inert. With the guard
        // down it plays nothing and the base layer shows through untouched.
        var none = sm.AddState("NoBlock");
        var hold = sm.AddState("BlockHold");
        hold.motion = idle;
        sm.defaultState = none;

        var toHold = none.AddTransition(hold);
        toHold.AddCondition(AnimatorConditionMode.If, 0f, "isBlocking");
        toHold.hasExitTime = false;
        toHold.duration = 0.12f;

        var toNone = hold.AddTransition(none);
        toNone.AddCondition(AnimatorConditionMode.IfNot, 0f, "isBlocking");
        toNone.hasExitTime = false;
        toNone.duration = 0.15f;

        if (hit != null)
        {
            var flinch = sm.AddState("BlockImpact");
            flinch.motion = hit;

            var toFlinch = hold.AddTransition(flinch);
            toFlinch.AddCondition(AnimatorConditionMode.If, 0f, "BlockHit");
            toFlinch.hasExitTime = false;
            toFlinch.duration = 0.03f;

            var backToHold = flinch.AddTransition(hold);
            backToHold.hasExitTime = true;
            backToHold.exitTime = 0.7f;
            backToHold.duration = 0.1f;
        }

        ctrl.AddLayer(layer);
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();

        Debug.Log("[Shields] Added a 'Block' override layer to the player's animator with isBlocking + BlockHit. " +
                  "It is unmasked, so it drives the whole body — if the legs should keep running while guarding, " +
                  "assign an upper-body avatar mask to that layer in the Animator window.");
    }

    private static bool HasParameter(AnimatorController c, string name)
    {
        foreach (var p in c.parameters) if (p.name == name) return true;
        return false;
    }
}
