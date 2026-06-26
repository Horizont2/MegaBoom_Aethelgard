using UnityEngine;
using UnityEngine.UI;

// Rotates a UI arrow to match the player's world yaw. Drop on the
// mini-map GameObject and wire `arrowImage` to whatever arrow sprite
// you've placed in your mini-map UI.
//
// The mini-map camera renders straight down, so the player's
// world-Y eulerAngle IS the screen-space rotation we want — but
// inverted because UI Z+ goes the opposite way from world Z+.
public class MinimapPlayerArrow : MonoBehaviour
{
    [Tooltip("Player transform whose yaw drives the arrow. Auto-finds via 'Player' tag if null.")]
    public Transform player;
    [Tooltip("The UI arrow Image. Will rotate around its pivot to match player yaw.")]
    public Image arrowImage;
    [Tooltip("Offset added to the rotation (degrees). Use if your arrow art doesn't point 'up' at 0°.")]
    public float rotationOffset = 0f;

    private RectTransform arrowRT;

    private void Start()
    {
        if (arrowImage != null) arrowRT = arrowImage.GetComponent<RectTransform>();
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
    }

    private void LateUpdate()
    {
        if (arrowRT == null || player == null) return;
        float yaw = player.eulerAngles.y;
        arrowRT.localRotation = Quaternion.Euler(0f, 0f, -yaw + rotationOffset);
    }
}
