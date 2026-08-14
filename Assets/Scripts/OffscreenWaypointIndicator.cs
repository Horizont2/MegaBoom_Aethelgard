using UnityEngine;
using UnityEngine.UI;

// Screen-edge arrow that clamps to whatever world target you point at.
// When the target is on-screen: shows a small marker over it. When it's
// off-screen: parks a rotated arrow on the closest screen edge pointing
// toward the target, plus a distance label. Fills the gap the compass /
// minimap don't cover on-screen.
//
// Zero-config: attach anywhere on a screen-space canvas — the component
// auto-creates its arrow / label UI on Start if you don't wire them.
// Set `target` at runtime from the guide / mission code:
//   OffscreenWaypointIndicator.Instance.SetTarget(t);
public class OffscreenWaypointIndicator : MonoBehaviour
{
    public static OffscreenWaypointIndicator Instance { get; private set; }

    [Header("Wiring (all optional — auto-created if null)")]
    public Canvas parentCanvas;
    public RectTransform arrow;        // rotated toward target when off-screen
    public RectTransform onScreenIcon; // ring / dot on the target when on-screen
    public TMPro.TextMeshProUGUI distanceLabel;

    [Header("Behaviour")]
    [Tooltip("Padding from screen edge in pixels — keeps the arrow inside the safe area.")]
    public float edgePadding = 60f;
    [Tooltip("Fade alpha for the on-screen indicator so it doesn't obscure the target.")]
    [Range(0.1f, 1f)] public float onScreenAlpha = 0.55f;
    [Tooltip("Hide entirely when the target is closer than this (m). 0 = always show.")]
    public float hideWithinMeters = 3f;
    [Tooltip("Auto-generate placeholder arrow/dot graphics when none are wired. " +
             "OFF by default — the placeholders are sprite-less UI Images, which " +
             "render as an ugly solid gold SQUARE floating over the objective. " +
             "Wire real arrow/onScreenIcon sprites and this stays off; the mission " +
             "plate + trail already guide the player meanwhile.")]
    public bool autoBuildPlaceholder = false;

    private Transform target;
    private Camera mainCam;
    private int lastShownMeters = -1;

    private void Awake()
    {
        Instance = this;
        if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();
    }

    private void Start()
    {
        mainCam = CameraCache.Main;
        // Only build the placeholder squares when explicitly asked. Without
        // a wired sprite they render as a solid gold box over the target
        // ("the incomprehensible objective square on the map table"), so by
        // default we leave arrow/onScreenIcon null and LateUpdate simply
        // draws nothing until a real sprite is assigned.
        if (autoBuildPlaceholder && (arrow == null || onScreenIcon == null)) BuildFallbackUI();
        SetVisible(false);
    }

    public void SetTarget(Transform t) => target = t;
    public void ClearTarget() { target = null; SetVisible(false); }

    private void LateUpdate()
    {
        if (target == null) { SetVisible(false); return; }
        if (mainCam == null) mainCam = CameraCache.Main;
        if (mainCam == null) return;

        Vector3 worldPos = target.position + Vector3.up * 1.2f;

        // Distance check + hide-if-close
        if (hideWithinMeters > 0f)
        {
            var pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null)
            {
                float d = Vector3.Distance(pObj.transform.position, target.position);
                if (d < hideWithinMeters) { SetVisible(false); return; }
                UpdateDistance(d);
            }
        }

        Vector3 screen = mainCam.WorldToScreenPoint(worldPos);
        bool behind = screen.z < 0f;
        bool onScreen = !behind && screen.x >= 0 && screen.x <= Screen.width && screen.y >= 0 && screen.y <= Screen.height;

        if (onScreen)
        {
            if (arrow != null) arrow.gameObject.SetActive(false);
            if (onScreenIcon != null)
            {
                onScreenIcon.gameObject.SetActive(true);
                Vector2 canvasPos;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    (RectTransform)parentCanvas.transform, screen, parentCanvas.worldCamera, out canvasPos);
                onScreenIcon.anchoredPosition = canvasPos;
                var img = onScreenIcon.GetComponent<Image>();
                if (img != null) { var c = img.color; c.a = onScreenAlpha; img.color = c; }
            }
        }
        else
        {
            if (onScreenIcon != null) onScreenIcon.gameObject.SetActive(false);
            if (arrow == null) return;
            arrow.gameObject.SetActive(true);
            // Flip when behind — WorldToScreenPoint gives inverted x when z<0.
            if (behind) { screen.x = Screen.width - screen.x; screen.y = Screen.height - screen.y; }

            // Clamp screen point to inner rect (padded edge).
            float cx = Mathf.Clamp(screen.x, edgePadding, Screen.width - edgePadding);
            float cy = Mathf.Clamp(screen.y, edgePadding, Screen.height - edgePadding);
            Vector2 clampedScreen = new Vector2(cx, cy);

            Vector2 canvasPos;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)parentCanvas.transform, clampedScreen, parentCanvas.worldCamera, out canvasPos);
            arrow.anchoredPosition = canvasPos;

            // Rotate arrow toward target direction on screen.
            Vector2 dir = new Vector2(screen.x - Screen.width * 0.5f, screen.y - Screen.height * 0.5f);
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            arrow.localRotation = Quaternion.Euler(0f, 0f, ang);
        }
    }

    private void UpdateDistance(float d)
    {
        if (distanceLabel == null) return;
        int m = Mathf.RoundToInt(d);
        if (m == lastShownMeters) return;
        lastShownMeters = m;
        distanceLabel.text = m + "m";
    }

    private void SetVisible(bool v)
    {
        if (arrow != null && arrow.gameObject.activeSelf != v) arrow.gameObject.SetActive(v);
        if (onScreenIcon != null && onScreenIcon.gameObject.activeSelf != v) onScreenIcon.gameObject.SetActive(v);
        if (distanceLabel != null && distanceLabel.gameObject.activeSelf != v) distanceLabel.gameObject.SetActive(v);
    }

    // Auto-create arrow + on-screen indicator so no Inspector wiring is
    // required — designer can override any of these fields with a nicer
    // sprite / prefab later.
    private void BuildFallbackUI()
    {
        if (parentCanvas == null) return;

        // Arrow — a triangle-ish square (rotated) with a warm gold tint.
        var arrowGO = new GameObject("WaypointArrow", typeof(RectTransform), typeof(Image));
        arrowGO.transform.SetParent(parentCanvas.transform, false);
        arrow = (RectTransform)arrowGO.transform;
        arrow.sizeDelta = new Vector2(48f, 48f);
        var arrowImg = arrowGO.GetComponent<Image>();
        arrowImg.color = new Color(1f, 0.85f, 0.35f, 0.9f);
        arrowImg.raycastTarget = false;

        // On-screen dot — same tint at low alpha.
        var dotGO = new GameObject("WaypointDot", typeof(RectTransform), typeof(Image));
        dotGO.transform.SetParent(parentCanvas.transform, false);
        onScreenIcon = (RectTransform)dotGO.transform;
        onScreenIcon.sizeDelta = new Vector2(32f, 32f);
        var dotImg = dotGO.GetComponent<Image>();
        dotImg.color = new Color(1f, 0.85f, 0.35f, onScreenAlpha);
        dotImg.raycastTarget = false;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
