using UnityEngine;

public class MinimapCamera : MonoBehaviour
{
    public Transform player;
    public float cameraHeight = 50f;
    [Tooltip("Чи обертати карту разом з гравцем (AAA стиль)")]
    public bool rotateWithPlayer = true;

    // ==== THE MINIMAP WAS RE-RENDERING THE WORLD EVERY FRAME ====
    //
    // Its culling mask includes Default, so every tree, rock and building within
    // the orthographic box around the player was being submitted a SECOND time
    // each frame, into a 512x512 target that until now also carried 8x MSAA, a
    // full mip chain and an HDR format - for a picture shown at roughly 220 UI
    // pixels. Those three are fixed on the asset; this is the other half.
    //
    // A minimap does not need sixty updates a second. The player moves a few
    // metres in that time and the image is a top-down box fifteen metres across,
    // so at 20Hz the difference is not perceptible - but two thirds of the
    // second render pass simply stops happening.
    //
    // The TRANSFORM still follows every frame, in LateUpdate below, so the
    // player blip never lags behind the world. Only the RENDER is rate-limited.
    [Tooltip("How many times a second the minimap image is redrawn. The camera still follows the player every frame; this only limits the render.")]
    [Range(5f, 60f)] public float renderHz = 20f;

    private Camera _cam;
    private float _nextRender;

    private void Start()
    {
        transform.parent = null;

        _cam = GetComponent<Camera>();
        if (_cam != null) _cam.enabled = false;   // we drive it by hand from now on
    }

    private void LateUpdate()
    {
        if (player != null)
        {
            // Камера просто висить над гравцем
            transform.position = new Vector3(player.position.x, player.position.y + cameraHeight, player.position.z);

            // Завжди дивиться вниз (Північ зверху)
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        // Render on our own clock, AFTER the follow has moved us, so the frame
        // that gets drawn is never one update stale.
        if (_cam == null) return;
        if (Time.unscaledTime < _nextRender) return;
        _nextRender = Time.unscaledTime + 1f / Mathf.Max(1f, renderHz);
        _cam.Render();
    }

    private void OnDisable()
    {
        // Leave it as we found it, so a scene that expects an ordinary camera
        // (or an editor preview) is not handed a permanently dark one.
        if (_cam != null) _cam.enabled = true;
    }
}