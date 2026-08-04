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

    public static bool TryShake(float intensity, float duration)
    {
        var cf = GetFollow();
        if (cf == null) return false;
        cf.TriggerShake(intensity, duration);
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
