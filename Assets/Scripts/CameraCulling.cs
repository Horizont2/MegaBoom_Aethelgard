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

    // ==== THIS WAS IN NO SCENE AT ALL ====
    //
    // WorldGenerator.TagAsNature moves every collider-less rock, bush, log, water
    // plant and scatter prop onto the Nature layer, and its tooltip says out loud
    // that it does so "so CameraCulling's per-layer distance actually applies".
    // A GUID search across every .unity and .prefab returns nothing: the
    // component was never placed, nothing ever called AddComponent for it, and
    // Camera.layerCullDistances was therefore never set. All of that clutter has
    // been rendering to the camera's 900m far plane.
    //
    // Self-installing on the main camera closes that, and means it cannot be
    // lost again by a scene edit.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallOnMainCamera()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) => Attach();
        Attach();
    }

    private static void Attach()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var cc = cam.GetComponent<CameraCulling>();
        if (cc == null) cc = cam.gameObject.AddComponent<CameraCulling>();
        cc.ApplyFromSettings();
    }

    // The distance is a PRESET lever, which is the whole point: Ultra keeps a
    // generous leash and only the lower presets tighten it. Nothing about what
    // Ultra renders changes.
    public void ApplyFromSettings()
    {
        // Same fallback as SettingsApplier.ApplyFoliageDetail: nothing in the
        // project writes Settings_FoliageDetail yet, so follow the quality level
        // the player can actually move until a control exists for it.
        int q = PlayerPrefs.HasKey("Settings_FoliageDetail")
              ? Mathf.Clamp(PlayerPrefs.GetInt("Settings_FoliageDetail"), 0, 3)
              : Mathf.Clamp(PlayerPrefs.GetInt("Settings_QualityLevel", 1), 0, 3);
        // ==== THE CAMP'S TREES ARE ON THIS LAYER TOO ====
        //
        // The comment at the top of this file says only tiny props are on
        // Nature, so the distance can be generous. That is true of the generated
        // region, where TagAsNature only moves collider-less clutter. It is NOT
        // true of the camp: all 800-odd birch trees there sit on layer 15, and
        // the camp is 219 x 166 metres — so 110m at Performance would cut the
        // tree line in half and 170m would clip its corners.
        //
        // A hand-built hub gets distances that clear it. Only the procedural
        // region, where this really is only ground clutter, gets the short ones.
        bool handBuiltScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "GameScene";
        natureRenderDistance = handBuiltScene
            ? q switch { 0 => 260f, 1 => 320f, 2 => 420f, _ => 600f }
            : q switch { 0 => 110f, 1 => 170f, 2 => 260f, _ => 400f };
        Apply();
    }

    private void Start() { ApplyFromSettings(); }

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
