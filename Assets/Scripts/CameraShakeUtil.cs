using UnityEngine;

// Central shake helper. Callers all over the codebase used to do
//   if (Camera.main != null) Camera.main.GetComponent<CameraFollow>().TriggerShake(...)
// which null-checked the camera but NOT the CameraFollow component —
// so a pause-scene or cinematic camera without CameraFollow would NRE
// mid-cutscene, aborting region conquest / totem activation / boss
// sequences with the softlock that came with them.
//
// TryShake looks up the CameraFollow through the (cached) main camera
// and no-ops silently when either is missing.
public static class CameraShakeUtil
{
    private static Camera s_cachedCamera;
    private static CameraFollow s_cachedFollow;

    private static CameraFollow GetFollow()
    {
        Camera cam = CameraCache.Main;
        if (cam == null) { s_cachedCamera = null; s_cachedFollow = null; return null; }
        if (cam != s_cachedCamera)
        {
            s_cachedCamera = cam;
            s_cachedFollow = cam.GetComponent<CameraFollow>();
        }
        return s_cachedFollow;
    }

    // ==== THESE TWO WERE THE WRONG WAY ROUND ====
    //
    // This takes (intensity, duration) and forwarded them POSITIONALLY into
    // CameraFollow.TriggerShake(float duration, float intensity). So every
    // shake routed through here ran the intensity as a duration and the
    // duration as an intensity — and that is around twenty-five call sites:
    // boss death, boss enrage, guard break, block impact, and everything in
    // RegionTotem, RegionManager, TutorialBossAI, CorruptionAnchor and
    // CagedAllyEvent.
    //
    // The direct TriggerShake callers were always right, which is why this hid:
    // PlayerController, GrenadeLogic and TutorialBossAI all pass
    // (duration, intensity) to the real method and shake correctly.
    //
    // A guard break asks for TryShake(0.9f, 0.4f) — a hard 0.9 whack lasting
    // 0.4s. What it got was a 0.4-amplitude wobble lasting nine tenths of a
    // second: a drift instead of a blow. Named arguments now, so this cannot
    // come back.
    public static bool TryShake(float intensity, float duration)
    {
        var cf = GetFollow();
        if (cf == null) return false;
        cf.TriggerShake(duration: duration, intensity: intensity);
        return true;
    }

    public static bool TryDirectionalShake(Vector3 direction, float intensity, float duration, float verticalBias = 0f)
    {
        var cf = GetFollow();
        if (cf == null) return false;
        cf.TriggerDirectionalShake(direction, intensity, duration, verticalBias);
        return true;
    }
}
