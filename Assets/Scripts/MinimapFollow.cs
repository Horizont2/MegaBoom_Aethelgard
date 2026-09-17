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
    // ==== A MOVING VIEWPORT IS NOT A STATIC IMAGE ====
    //
    // 20Hz was wrong, and the reason is worth writing down. Rate-limiting works
    // when the picture is mostly still, because the eye only notices the frames
    // that differ. This camera TRANSLATES with the player every frame, so every
    // pixel of the image shifts between renders - and a whole image that jumps
    // in steps reads as the framerate itself having collapsed, which is exactly
    // what it looked like.
    //
    // So the rate limit is a weak lever here and it is set gently. The real
    // saving was never this: it was the 8x MSAA, the mip chain rebuilt every
    // frame and the HDR format on a 512x512 target shown at about 220 pixels,
    // and all three of those are gone regardless of this number.
    //
    // Set it to 60 to disable the limit entirely if any judder remains.
    //
    // The TRANSFORM still follows every frame, in LateUpdate below, so the
    // player blip never lags behind the world. Only the RENDER is rate-limited.
    [Tooltip("How many times a second the minimap image is redrawn. The camera still follows the player every frame; this only limits the render.")]
    [Range(15f, 60f)] public float renderHz = 45f;

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

        // Nothing to draw into: if the minimap image is switched off - the
        // fullscreen map is open, a menu is up, the HUD is hidden - the second
        // render pass is pure waste. This is the saving that costs nothing in
        // smoothness, because there is nothing to be smooth.
        if (!MinimapVisible()) return;

        if (Time.unscaledTime < _nextRender) return;
        _nextRender = Time.unscaledTime + 1f / Mathf.Max(1f, renderHz);
        _cam.Render();
    }

    // The RawImage the render texture is shown on. Resolved once; if there is
    // no such image we render anyway rather than silently going dark.
    private UnityEngine.UI.RawImage _display;
    private bool _displaySearched;

    private bool MinimapVisible()
    {
        if (!_displaySearched)
        {
            _displaySearched = true;
            foreach (var img in Object.FindObjectsByType<UnityEngine.UI.RawImage>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (img != null && _cam != null && img.texture == _cam.targetTexture) { _display = img; break; }
            }
        }
        if (_display == null) return true;
        return _display.isActiveAndEnabled && _display.canvas != null && _display.canvas.isActiveAndEnabled;
    }

    private void OnDisable()
    {
        // Leave it as we found it, so a scene that expects an ordinary camera
        // (or an editor preview) is not handed a permanently dark one.
        if (_cam != null) _cam.enabled = true;
    }
}