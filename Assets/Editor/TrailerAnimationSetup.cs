using System.Linq;
using UnityEditor;
using UnityEngine;

// Assigns the cutscene clips + per-clip rig masks to the trailer's
// TrailerRideEvent components, which play them DIRECTLY on the animators
// (PlayableGraph) — no controller states/triggers, so they can't silently fail.
//
//   Tools ▸ Lore Trailer ▸ Setup Cutscene Animations
//
// Clip resolution order (so it keeps working after the rigs were re-imported and
// the clips now live INSIDE their FBX rigs):
//   1. the clip embedded in the rig FBX / the animator's own controller,
//   2. the standalone .anim next to it,
// matched by name, case-insensitively.
public static class TrailerAnimationSetup
{
    private const string UpperBodyMaskPath = "Assets/LoreTrailer/TrailerUpperBody.mask";
    private const string FullBodyMaskPath = "Assets/LoreTrailer/TrailerFullBody.mask";

    private const string HeroAnimFolder = "Assets/HeroAnimations";
    private const string HorseAnimFolder = "Assets/LPHorse_Version_2_9";
    private const string HeroController = "Assets/MainCharacters/Animations/fbx/HeroAnimator.controller";

    [MenuItem("Tools/Lore Trailer/Setup Cutscene Animations")]
    public static void Setup()
    {
        var events = Object.FindObjectsByType<TrailerRideEvent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (events.Length == 0)
        {
            EditorUtility.DisplayDialog("Cutscene Animations",
                "No TrailerRideEvent found — run 'Setup Part 2' first, then this.", "OK");
            return;
        }

        // Prefer clips that live on the actual rigs in the scene (the animator's
        // controller), then anything in the source folders.
        var ride = Object.FindFirstObjectByType<TrailerHorseRide>();
        Animator horseAnimator = null, riderAnimator = null;
        if (ride != null)
        {
            horseAnimator = ride.GetComponent<Animator>() ?? ride.GetComponentsInChildren<Animator>(true).FirstOrDefault();
            riderAnimator = ride.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a != horseAnimator);
        }

        var look = Resolve("Look behind", riderAnimator, HeroAnimFolder, "look", "behind");
        var fall = Resolve("Falling back", riderAnimator, HeroAnimFolder, "falling", "back");
        var rear = Resolve("RearUp", horseAnimator, HorseAnimFolder, "rear");

        var hero = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(HeroController);
        var upper = BuildMask(UpperBodyMaskPath, upperBodyOnly: true);
        var full = BuildMask(FullBodyMaskPath, upperBodyOnly: false);

        foreach (var e in events)
        {
            Undo.RecordObject(e, "assign cutscene clips");
            e.lookBehindClip = look;
            e.fallingBackClip = fall;
            e.horseRearClip = rear;
            e.upperBodyMask = upper;   // glance back: legs keep riding
            e.fallMask = full;         // fall: whole body leaves the saddle
            e.horseMask = null;        // horse rear: no mask (generic rig)
            e.lookBackWeight = 0.85f;
            e.heroAnimator = hero;      // restored once he is back on his feet
            EditorUtility.SetDirty(e);
        }
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Cutscene Animations",
            $"Assigned clips to {events.Length} TrailerRideEvent(s):\n" +
            $"  • Look behind: {Describe(look)}\n" +
            $"  • Falling back: {Describe(fall)}\n" +
            $"  • Horse rear: {Describe(rear)}\n" +
            $"  • Hero controller (restored after the get-up): {(hero != null ? hero.name : "MISSING")}\n\n" +
            "Rig per clip:\n" +
            "  • Look behind — UPPER BODY only (root + legs + foot IK off), blended\n" +
            "    at 85% over the riding pose, so he stays seated and only glances back.\n" +
            "  • Falling back — FULL body, held on the last frame.\n" +
            "  • Horse rear — full body on the horse, held so it stands reared.", "OK");
    }

    private static string Describe(AnimationClip c)
    {
        if (c == null) return "MISSING";
        string p = AssetDatabase.GetAssetPath(c);
        return $"{c.name}  ({System.IO.Path.GetFileName(p)})";
    }

    // Find a clip by name: first on the rig in the scene, then anywhere under the
    // source folder (this picks up clips embedded in the FBX rigs).
    private static AnimationClip Resolve(string preferredName, Animator rig, string folder, params string[] needles)
    {
        if (rig != null && rig.runtimeAnimatorController != null)
        {
            var onRig = rig.runtimeAnimatorController.animationClips
                .Where(c => c != null)
                .FirstOrDefault(c => Matches(c.name, preferredName, needles));
            if (onRig != null) return onRig;
        }

        if (!AssetDatabase.IsValidFolder(folder)) return null;

        var candidates = AssetDatabase.FindAssets("t:AnimationClip", new[] { folder })
            .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
            .OfType<AnimationClip>()
            .Where(c => c != null && !c.name.StartsWith("__preview__"))
            .ToArray();

        // Exact name first, then the needle match.
        return candidates.FirstOrDefault(c => string.Equals(c.name, preferredName, System.StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(c => Matches(c.name, preferredName, needles));
    }

    private static bool Matches(string name, string preferred, string[] needles)
    {
        string n = name.ToLowerInvariant();
        if (n == preferred.ToLowerInvariant()) return true;
        foreach (var s in needles) if (!n.Contains(s.ToLowerInvariant())) return false;
        return needles.Length > 0;
    }

    private static AvatarMask BuildMask(string path, bool upperBodyOnly)
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
        if (mask == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/LoreTrailer")) AssetDatabase.CreateFolder("Assets", "LoreTrailer");
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, path);
        }

        bool lower = !upperBodyOnly;
        // Root off on the upper-body mask so the glance can't slide the rider out
        // of the saddle; on for the full-body mask so the fall reads properly.
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, lower);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, lower);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, lower);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, lower);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, lower);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, false);
        EditorUtility.SetDirty(mask);
        return mask;
    }
}
