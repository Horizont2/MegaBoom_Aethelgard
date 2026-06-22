using UnityEngine;
using UnityEngine.UI;

// Procedurally adds a yaw-tracking arrow overlay at the centre of the
// mini-map. The minimap camera renders straight down, so previously
// the player dot didn't show facing direction — the player had to
// reason about where they were looking. The arrow points "forward"
// in screen-space and rotates with the player's yaw.
//
// Drop on the mini-map's RawImage / RectTransform. If no player is
// wired, hooks the camp's tagged Player on first frame.
public class MinimapPlayerArrow : MonoBehaviour
{
    [Tooltip("Player transform whose yaw drives the arrow. Auto-finds via 'Player' tag if null.")]
    public Transform player;
    [Tooltip("Arrow size in pixels.")]
    public float arrowSize = 28f;
    [Tooltip("Arrow tint.")]
    public Color arrowColor = new Color(0.4f, 0.85f, 1f, 0.95f);

    private RectTransform arrowRT;

    private void Start()
    {
        BuildArrowIfNeeded();
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
    }

    private void LateUpdate()
    {
        if (arrowRT == null) BuildArrowIfNeeded();
        if (player == null || arrowRT == null) return;

        // The minimap camera looks straight down (rotation 90,0,0), so
        // the player's world-Y rotation IS the screen-space rotation
        // we want — but inverted because UI Z+ is up vs world Z+.
        float yaw = player.eulerAngles.y;
        arrowRT.localRotation = Quaternion.Euler(0f, 0f, -yaw);
    }

    private void BuildArrowIfNeeded()
    {
        if (arrowRT != null) return;
        RectTransform parentRT = GetComponent<RectTransform>();
        if (parentRT == null) return;

        GameObject go = new GameObject("PlayerArrow");
        arrowRT = go.AddComponent<RectTransform>();
        arrowRT.SetParent(parentRT, false);
        arrowRT.anchorMin = new Vector2(0.5f, 0.5f);
        arrowRT.anchorMax = new Vector2(0.5f, 0.5f);
        arrowRT.pivot = new Vector2(0.5f, 0.5f);
        arrowRT.sizeDelta = new Vector2(arrowSize, arrowSize);
        arrowRT.SetAsLastSibling();

        Image img = go.AddComponent<Image>();
        img.sprite = BuildArrowSprite();
        img.color = arrowColor;
        img.raycastTarget = false;
        img.preserveAspect = true;
    }

    // Procedural triangle (pointing up) — saves us an art asset.
    private static Sprite s_arrowSprite;
    private static Sprite BuildArrowSprite()
    {
        if (s_arrowSprite != null) return s_arrowSprite;
        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] px = new Color[size * size];
        // Triangle apex at (cx, top), base across the bottom 60%.
        for (int y = 0; y < size; y++)
        {
            float v = y / (float)(size - 1);
            float halfWidth = (1f - v) * (size * 0.45f); // narrows as y grows toward apex
            float cx = size / 2f;
            for (int x = 0; x < size; x++)
            {
                bool inside = Mathf.Abs(x - cx) <= halfWidth && y < size * 0.95f && y > size * 0.05f;
                bool border = inside && (Mathf.Abs(x - cx) > halfWidth - 1.5f || y < size * 0.08f);
                if (inside) px[y * size + x] = border ? new Color(0f, 0f, 0f, 1f) : new Color(1f, 1f, 1f, 1f);
                else px[y * size + x] = new Color(0f, 0f, 0f, 0f);
            }
        }
        tex.SetPixels(px);
        tex.Apply(false);
        s_arrowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return s_arrowSprite;
    }
}
