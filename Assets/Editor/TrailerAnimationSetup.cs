using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Wires the cutscene animations added to the project.
//
//   Tools ▸ Lore Trailer ▸ Setup Cutscene Animations
//
// Horse: adds a "Rear" trigger onto the rear-up state in Horse_Animator.
// Rider (OnHorseAnimator): adds an UPPER-BODY-masked "Look behind" layer (legs
// keep the riding pose — the requested "remove lower body on look-back"), plus
// Fall / GetUp / Attack states with triggers. TrailerRideEvent fires them.
public static class TrailerAnimationSetup
{
    private const string HorseController = "Assets/Animators/Horse_Animator.controller";
    private const string RearClip = "Assets/LPHorse_Version_2_9/Version_2_9/Animations/Primary_Actions/Rig_RearUp_Full_Right.anim";

    private const string RiderController = "Assets/Animators/OnHorseAnimator.controller";
    private const string MaskPath = "Assets/LoreTrailer/TrailerUpperBody.mask";
    private const string LookBehind = "Assets/HeroAnimations/Animations/Look behind.anim";
    private const string FallingBack = "Assets/HeroAnimations/Animations/Falling back.anim";
    private const string GettingUp = "Assets/HeroAnimations/Animations/Getting up.anim";
    private const string AttackClip = "Assets/HeroAnimations/Animations/Melee_1H_Attack_Slice_Horizontal.anim";

    [MenuItem("Tools/Lore Trailer/Setup Cutscene Animations")]
    public static void Setup()
    {
        int changes = SetupHorseRear();
        changes += SetupHeroAnims();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Cutscene Animations",
            "Wired the cutscene animations:\n" +
            "  • Horse: 'Rear' trigger on the rear-up state (AnyState → Rear → Run).\n" +
            "  • Rider: an UPPER-BODY-masked 'Look behind' layer (legs keep riding), plus Fall / GetUp / Attack states.\n" +
            "  • Triggers: LookBack, Fall, GetUp, Attack, Rear — fired by TrailerRideEvent.\n\n" +
            (changes == 0 ? "(No changes — already set up.)" : $"({changes} change(s) applied.)"), "OK");
    }

    // --- Rider (hero) ---

    private static int SetupHeroAnims()
    {
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(RiderController);
        if (ac == null) { Debug.LogWarning("[Trailer] OnHorseAnimator not found."); return 0; }

        var lookBehind = AssetDatabase.LoadAssetAtPath<AnimationClip>(LookBehind);
        var fall = AssetDatabase.LoadAssetAtPath<AnimationClip>(FallingBack);
        var getUp = AssetDatabase.LoadAssetAtPath<AnimationClip>(GettingUp);
        var attack = AssetDatabase.LoadAssetAtPath<AnimationClip>(AttackClip);

        int changes = 0;

        // Base-layer one-shot states (full body): Fall (stays down), GetUp, Attack.
        var baseSm = ac.layers[0].stateMachine;
        changes += AddTriggeredState(ac, baseSm, "Fall", fall, returnTo: null);
        changes += AddTriggeredState(ac, baseSm, "GetUp", getUp, returnTo: baseSm.defaultState);
        changes += AddTriggeredState(ac, baseSm, "Attack", attack, returnTo: baseSm.defaultState);

        // Upper-body masked look-back layer.
        changes += EnsureLookBackLayer(ac, lookBehind);

        if (changes > 0) EditorUtility.SetDirty(ac);
        return changes;
    }

    private static int EnsureLookBackLayer(AnimatorController ac, AnimationClip lookBehind)
    {
        if (lookBehind == null) return 0;
        if (ac.layers.Any(l => l.name == "TrailerLookBack")) return 0;

        if (!ac.parameters.Any(p => p.name == "LookBack"))
            ac.AddParameter("LookBack", AnimatorControllerParameterType.Trigger);

        var mask = LoadOrCreateUpperBodyMask();

        var sm = new AnimatorStateMachine { name = "TrailerLookBack", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(sm, ac);
        var empty = sm.AddState("Empty");
        var look = sm.AddState("LookBack");
        look.motion = lookBehind;
        sm.defaultState = empty;

        var toLook = sm.AddAnyStateTransition(look);
        toLook.AddCondition(AnimatorConditionMode.If, 0f, "LookBack");
        toLook.duration = 0.2f; toLook.hasExitTime = false; toLook.canTransitionToSelf = false;

        var back = look.AddTransition(empty);
        back.hasExitTime = true; back.exitTime = 0.8f; back.duration = 0.3f;

        ac.AddLayer(new AnimatorControllerLayer
        {
            name = "TrailerLookBack",
            defaultWeight = 1f,
            avatarMask = mask,
            blendingMode = AnimatorLayerBlendingMode.Override,
            stateMachine = sm,
        });
        return 1;
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
        // Upper body ON, lower body OFF — so the look-back never touches the legs.
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

    // AnyState -> state (on a trigger of the same name); optional return transition.
    private static int AddTriggeredState(AnimatorController ac, AnimatorStateMachine sm, string name, AnimationClip clip, AnimatorState returnTo)
    {
        if (clip == null) return 0;
        if (sm.states.Any(s => s.state.name == name)) return 0;
        if (!ac.parameters.Any(p => p.name == name))
            ac.AddParameter(name, AnimatorControllerParameterType.Trigger);

        var st = sm.AddState(name);
        st.motion = clip;
        var t = sm.AddAnyStateTransition(st);
        t.AddCondition(AnimatorConditionMode.If, 0f, name);
        t.duration = 0.15f; t.hasExitTime = false; t.canTransitionToSelf = false;
        if (returnTo != null)
        {
            var b = st.AddTransition(returnTo);
            b.hasExitTime = true; b.exitTime = 0.9f; b.duration = 0.25f;
        }
        return 1;
    }

    private static int SetupHorseRear()
    {
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(HorseController);
        if (ac == null) { Debug.LogWarning("[Trailer] Horse_Animator not found."); return 0; }
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RearClip);
        if (clip == null) { Debug.LogWarning("[Trailer] Rear-up clip not found."); return 0; }

        int changes = 0;

        // Trigger parameter.
        bool hasParam = false;
        foreach (var p in ac.parameters) if (p.name == "Rear") hasParam = true;
        if (!hasParam) { ac.AddParameter("Rear", AnimatorControllerParameterType.Trigger); changes++; }

        var sm = ac.layers[0].stateMachine;

        // Use the rear state the user already added (name contains "rear"); only
        // create one if none exists. Return to Idle (the horse STANDS after
        // rearing, not gallops on).
        AnimatorState rear = null, run = null;
        foreach (var cs in sm.states)
        {
            string n = cs.state.name.ToLowerInvariant();
            if (rear == null && n.Contains("rear")) rear = cs.state;
            if (cs.state.name == "Idle") run = cs.state;          // return target = Idle (stand)
        }
        if (run == null) foreach (var cs in sm.states) if (cs.state.name == "Run") run = cs.state;
        if (rear == null) { rear = sm.AddState("Rear"); rear.motion = clip; changes++; }
        else if (rear.motion == null) { rear.motion = clip; changes++; }

        // AnyState -> Rear (on trigger).
        bool hasAny = false;
        foreach (var t in sm.anyStateTransitions) if (t.destinationState == rear) hasAny = true;
        if (!hasAny)
        {
            var t = sm.AddAnyStateTransition(rear);
            t.AddCondition(AnimatorConditionMode.If, 0f, "Rear");
            t.duration = 0.12f; t.hasExitTime = false; t.canTransitionToSelf = false;
            changes++;
        }

        // Rear -> Run (return after the rear plays out).
        if (run != null)
        {
            bool hasBack = false;
            foreach (var t in rear.transitions) if (t.destinationState == run) hasBack = true;
            if (!hasBack)
            {
                var t = rear.AddTransition(run);
                t.hasExitTime = true; t.exitTime = 0.85f; t.duration = 0.25f;
                changes++;
            }
        }

        if (changes > 0) EditorUtility.SetDirty(ac);
        return changes;
    }
}
