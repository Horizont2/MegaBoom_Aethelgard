using System.Collections.Generic;
using UnityEngine;

// Animator helpers that no-op when the requested parameter doesn't
// exist on the controller. Lets us drop SetFloat("Speed", v) calls on
// shared movement scripts without each call site checking whether the
// specific animator was authored with the parameter. Without these,
// every frame on every NPC without a Speed parameter spammed
// "Parameter 'Speed' does not exist" warnings.
//
// PERF: HasParameter used to read anim.parameters (which allocates a
// FRESH ARRAY on every access) and linear-scan by string. A camp with
// 6-8 NPCs each firing 3-5 Set*Safe calls per Update meant hundreds of
// per-frame allocs + O(N·params) scans. Now cached per controller —
// on first touch we snapshot the parameter set into a HashSet<int>
// keyed on Animator.StringToHash(name) and reuse it forever.
public static class AnimatorSafeExtensions
{
    // Cache is keyed on the RuntimeAnimatorController instance ID so a
    // scene reload / new controller variant repopulates correctly.
    private static readonly Dictionary<int, HashSet<int>> s_paramCache
        = new Dictionary<int, HashSet<int>>();

    public static void SetFloatSafe(this Animator anim, string name, float value)
    {
        if (anim == null || !anim.isActiveAndEnabled) return;
        if (!HasParameter(anim, name)) return;
        anim.SetFloat(Animator.StringToHash(name), value);
    }

    public static void SetBoolSafe(this Animator anim, string name, bool value)
    {
        if (anim == null || !anim.isActiveAndEnabled) return;
        if (!HasParameter(anim, name)) return;
        anim.SetBool(Animator.StringToHash(name), value);
    }

    public static void SetTriggerSafe(this Animator anim, string name)
    {
        if (anim == null || !anim.isActiveAndEnabled) return;
        if (!HasParameter(anim, name)) return;
        anim.SetTrigger(Animator.StringToHash(name));
    }

    public static bool HasParameter(Animator anim, string name)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return false;
        // GetInstanceID() is an obsolete-error in Unity 6.5+. RuntimeHelpers.GetHashCode
        // gives a stable per-instance int (identity hash) and compiles on every Unity
        // version, so the same source builds on both 6.3 (local) and 6.5 (cloud).
        int controllerID = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(anim.runtimeAnimatorController);
        if (!s_paramCache.TryGetValue(controllerID, out var set))
        {
            // First touch — snapshot the whole parameter list ONCE. The
            // `anim.parameters` getter still allocates here (one-time
            // cost), but from now on lookups are hash-set O(1).
            set = new HashSet<int>();
            var pars = anim.parameters;
            for (int i = 0; i < pars.Length; i++) set.Add(pars[i].nameHash);
            s_paramCache[controllerID] = set;
        }
        return set.Contains(Animator.StringToHash(name));
    }
}
