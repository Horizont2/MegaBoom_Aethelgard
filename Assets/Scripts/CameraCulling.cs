using UnityEngine;

// Per-layer cull distances. Small clutter (fallen logs, water plants, ground
// foliage) is tagged onto the Nature layer by WorldGenerator and stops being
// drawn well before the camera's far plane; everything that gives the landscape
// its silhouette — trees, rocks, cliffs, buildings — is deliberately NOT on that
// layer and keeps rendering to the far plane, so the distance never reads as a
// bare field.
public class CameraCulling : MonoBehaviour
{
    [Tooltip("Distance at which small ground clutter stops drawing. Only tiny props are on the Nature layer, so this can be generous — trees and rocks are unaffected and always render to the camera's far plane.")]
    public float natureRenderDistance = 260f;

    [Tooltip("Cull by true radial distance instead of distance along the camera's forward axis. Without this, an object at the edge of the screen disappears sooner than the same object in the centre, which is what makes clutter look like it pops in as you turn.")]
    public bool sphericalCulling = true;

    private Camera cam;

    private void Start() { Apply(); }

    // Public so a settings change can re-apply it without a scene reload.
    public void Apply()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) return;

        int nature = LayerMask.NameToLayer("Nature");
        if (nature < 0)
        {
            Debug.LogWarning("[CameraCulling] No 'Nature' layer in the project — per-layer culling is inactive.");
            return;
        }

        // 0 means "use the camera's far plane", which is what every other layer
        // should keep. Only Nature gets a shorter leash.
        float[] distances = new float[32];
        distances[nature] = Mathf.Min(natureRenderDistance, cam.farClipPlane);

        cam.layerCullDistances = distances;
        cam.layerCullSpherical = sphericalCulling;
    }
}
