using System.Linq;
using UnityEditor;
using UnityEngine;

// Assigns the cutscene clips + upper-body mask to the trailer's TrailerRideEvent
// components, which play them DIRECTLY on the animators (PlayableGraph) — no
// controller states/triggers, so they can't silently fail.
//
//   Tools ▸ Lore Trailer ▸ Setup Cutscene Animations
public static class TrailerAnimationSetup
{
    private const string MaskPath = "Assets/LoreTrailer/TrailerUpperBody.mask";
    private const string LookBehind = "Assets/HeroAnimations/Animations/Look behind.anim";
    private const string FallingBack = "Assets/HeroAnimations/Animations/Falling back.anim";
    private const string HorseRear = "Assets/LPHorse_Version_2_9/Version_2_9/Animations/Primary_Actions/Rig_RearUp_Full_Right.anim";

    [MenuItem("Tools/Lore Trailer/Setup Cutscene Animations")]
    public static void Setup()
    {
        var look = AssetDatabase.LoadAssetAtPath<AnimationClip>(LookBehind);
        var fall = AssetDatabase.LoadAssetAtPath<AnimationClip>(FallingBack);
        var rear = AssetDatabase.LoadAssetAtPath<AnimationClip>(HorseRear);
        var mask = LoadOrCreateUpperBodyMask();

        var events = Object.FindObjectsByType<TrailerRideEvent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var e in events)
        {
            Undo.RecordObject(e, "assign cutscene clips");
            e.lookBehindClip = look;
            e.fallingBackClip = fall;
            e.horseRearClip = rear;
            e.upperBodyMask = mask;
            EditorUtility.SetDirty(e);
        }
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Cutscene Animations",
            $"Assigned clips to {events.Length} TrailerRideEvent(s):\n" +
            $"  • Look behind: {(look ? "OK" : "MISSING")}\n" +
            $"  • Falling back: {(fall ? "OK" : "MISSING")}\n" +
            $"  • Horse rear: {(rear ? "OK" : "MISSING")}\n" +
            $"  • Upper-body mask: {(mask ? "OK" : "MISSING")}\n\n" +
            (events.Length == 0 ? "⚠ No TrailerRideEvent found — run 'Setup Part 2' first.\n" : "") +
            "They play directly on the animators (no triggers). Look-back uses the\n" +
            "upper-body mask so the legs keep riding.", "OK");
    }

    private static AvatarMask LoadOrCreateUpperBodyMask()
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/LoreTrailer")) AssetDatabase.CreateFolder("Assets", "LoreTrailer");
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, MaskPath);
        }
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, false);
        EditorUtility.SetDirty(mask);
        return mask;
    }
}
