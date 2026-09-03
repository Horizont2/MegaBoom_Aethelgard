using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Wires the cutscene animations added to the project.
//
//   Tools ▸ Lore Trailer ▸ Setup Cutscene Animations
//
// Horse: adds a "Rear" state (rear-up clip) + a "Rear" trigger to Horse_Animator,
// with AnyState -> Rear -> back to Run, so the horse can rear on cue (the
// lightning strike). TrailerRideEvent fires the trigger.
public static class TrailerAnimationSetup
{
    private const string HorseController = "Assets/Animators/Horse_Animator.controller";
    private const string RearClip = "Assets/LPHorse_Version_2_9/Version_2_9/Animations/Primary_Actions/Rig_RearUp_Full_Right.anim";

    [MenuItem("Tools/Lore Trailer/Setup Cutscene Animations")]
    public static void Setup()
    {
        int changes = SetupHorseRear();
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Cutscene Animations",
            changes > 0
                ? "Horse rear-up wired: 'Rear' trigger + state added to Horse_Animator (AnyState → Rear → Run).\n" +
                  "TrailerRideEvent will fire it at the lightning strike.\n\n" +
                  "Hero clips (look-back / fall / combat) live in Assets/HeroAnimations as FBX sub-clips — tell me which hero prefab is the trailer rider and I'll wire those with an upper-body mask on the look-back."
                : "Horse rear already set up (no changes).", "OK");
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

        // Find/​create the Rear state and find Run.
        AnimatorState rear = null, run = null;
        foreach (var cs in sm.states)
        {
            if (cs.state.name == "Rear") rear = cs.state;
            if (cs.state.name == "Run") run = cs.state;
        }
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
