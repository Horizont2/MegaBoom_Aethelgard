using UnityEngine;

public class UIBillboard : MonoBehaviour
{
    void LateUpdate()
    {
        // Route through CameraCache instead of caching Camera.main in Start.
        // A billboard cached its camera once could keep a stale/null ref
        // after a scene reload (or when spawned from a pool) and silently
        // stop facing the camera. CameraCache resolves once per scene and
        // auto-invalidates on scene change, so this stays a cheap lookup.
        Transform cam = CameraCache.MainTransform;
        if (cam != null)
        {
            transform.LookAt(transform.position + cam.rotation * Vector3.forward,
                             cam.rotation * Vector3.up);
        }
    }
}