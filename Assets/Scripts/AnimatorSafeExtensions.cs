using UnityEngine;

// Animator helpers that no-op when the requested parameter doesn't
// exist on the controller. Lets us drop SetFloat("Speed", v) calls on
// shared movement scripts without each call site checking whether the
// specific animator was authored with the parameter. Without these,
// every frame on every NPC without a Speed parameter spammed
// "Parameter 'Speed' does not exist" warnings.
public static class AnimatorSafeExtensions
{
    public static void SetFloatSafe(this Animator anim, string name, float value)
    {
        if (anim == null || !anim.isActiveAndEnabled) return;
        // HasParameter walks the parameter list — cheap relative to the
        // warning spam it prevents. Cache locally per-controller if it
        // ever shows up on profiles.
        if (!HasParameter(anim, name)) return;
        anim.SetFloat(name, value);
    }

    public static void SetBoolSafe(this Animator anim, string name, bool value)
    {
        if (anim == null || !anim.isActiveAndEnabled) return;
        if (!HasParameter(anim, name)) return;
        anim.SetBool(name, value);
    }

    public static void SetTriggerSafe(this Animator anim, string name)
    {
        if (anim == null || !anim.isActiveAndEnabled) return;
        if (!HasParameter(anim, name)) return;
        anim.SetTrigger(name);
    }

    public static bool HasParameter(Animator anim, string name)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return false;
        var parameters = anim.parameters;
        for (int i = 0; i < parameters.Length; i++)
            if (parameters[i].name == name) return true;
        return false;
    }
}
