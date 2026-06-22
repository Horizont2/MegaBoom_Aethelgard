using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class SmoothCompass : MonoBehaviour
{
    [Header("Compass UI")]
    public RectTransform compassPanel; // The mask/container
    public TextMeshProUGUI compassTextPrefab; // The template for numbers

    [Header("Radar Icons")]
    public RectTransform markersParent;
    public GameObject markerIconPrefab;

    [Header("Settings")]
    public float pixelsPerDegree = 12f;
    public float compassViewAngle = 60f;

    [Header("AAA Polish")]
    [Tooltip("Font scale multiplier for cardinal letters (N/E/S/W)")]
    public float cardinalScale = 1.35f;
    [Tooltip("Font scale multiplier for the 30/60/120/... major ticks")]
    public float majorScale = 1.0f;
    [Tooltip("Font scale multiplier for the 15/45/... minor ticks")]
    public float minorScale = 0.7f;
    [Tooltip("Alpha applied to minor ticks (15°/45°/...) — they should sit under the majors.")]
    [Range(0f, 1f)] public float minorAlpha = 0.55f;
    [Tooltip("Range within the view where marks/icons stay fully opaque. Beyond this they fade to zero.")]
    public float fullOpacityRange = 0.65f;
    [Tooltip("Show a small TMP label under each radar icon listing its distance to the player in meters.")]
    public bool showDistanceOnIcons = true;
    [Tooltip("Off-screen arrow shown at left/right when a marker is behind the player.")]
    public bool showOffScreenArrows = true;
    [Tooltip("Reference transform used to compute distances. If null, falls back to the main camera.")]
    public Transform distanceReference;

    // Per-mark metadata so we can apply per-frame opacity/scale without
    // re-parsing text or recomputing rich-text colours every frame.
    private class CompassMark
    {
        public RectTransform rect;
        public TextMeshProUGUI text;
        public float baseAlpha = 1f;
        public Vector3 baseScale = Vector3.one;
    }

    private readonly List<CompassMark> compassMarks = new List<CompassMark>(24);
    public static List<Transform> activeMarkers = new List<Transform>();
    private readonly List<GameObject> spawnedIcons = new List<GameObject>();
    private readonly List<RectTransform> spawnedIconRects = new List<RectTransform>();
    private readonly List<TextMeshProUGUI> spawnedIconDistance = new List<TextMeshProUGUI>();
    private readonly List<CanvasGroup> spawnedIconCanvas = new List<CanvasGroup>();

    // Parallel pools for inactive icons — avoid Destroy/Instantiate churn
    // when the set of tracked markers churns (totems unlock/lock,
    // landmarks come into compass range, etc.).
    private readonly Stack<GameObject> iconPool = new Stack<GameObject>(8);
    private readonly Stack<RectTransform> iconPoolRects = new Stack<RectTransform>(8);
    private readonly Stack<TextMeshProUGUI> iconPoolDistance = new Stack<TextMeshProUGUI>(8);
    private readonly Stack<CanvasGroup> iconPoolCanvas = new Stack<CanvasGroup>(8);

    private RectTransform offscreenLeftArrow;
    private RectTransform offscreenRightArrow;

    private Transform mainCamera;
    private float halfCompassPixels;

    private void Start()
    {
        if (compassTextPrefab != null && compassTextPrefab.gameObject == this.gameObject)
        {
            Debug.LogError("COMPASS ERROR: Script is on the Prefab! Move script to CompassPanel.");
            return;
        }

        mainCamera = CameraCache.MainTransform;

        if (compassTextPrefab == null || compassPanel == null)
        {
            Debug.LogError("COMPASS ERROR: Missing UI references in Inspector!");
            return;
        }

        halfCompassPixels = compassViewAngle * pixelsPerDegree;

        for (int i = 0; i < 360; i += 15)
        {
            TextMeshProUGUI newMark = Instantiate(compassTextPrefab, compassPanel, false);
            newMark.alignment = TextAlignmentOptions.Center;
            newMark.text = GetCompassMark(i);

            RectTransform rt = newMark.GetComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.anchoredPosition = Vector3.zero;

            CompassMark mark = new CompassMark { rect = rt, text = newMark };
            ConfigureMarkVisuals(mark, i);
            compassMarks.Add(mark);
        }

        compassTextPrefab.gameObject.SetActive(false);

        if (showOffScreenArrows) CreateOffscreenArrows();
    }

    private void ConfigureMarkVisuals(CompassMark mark, int angle)
    {
        bool isCardinal = angle == 0 || angle == 90 || angle == 180 || angle == 270;
        bool isMajor = !isCardinal && (angle % 30 == 0);

        float scaleMul;
        if (isCardinal) { scaleMul = cardinalScale; mark.baseAlpha = 1f; mark.text.fontStyle = FontStyles.Bold; }
        else if (isMajor) { scaleMul = majorScale; mark.baseAlpha = 1f; }
        else { scaleMul = minorScale; mark.baseAlpha = minorAlpha; }

        Vector3 s = Vector3.one * scaleMul;
        mark.baseScale = s;
        mark.rect.localScale = s;
    }

    private void CreateOffscreenArrows()
    {
        if (compassPanel == null) return;
        offscreenLeftArrow = CreateArrow("CompassArrowLeft", true);
        offscreenRightArrow = CreateArrow("CompassArrowRight", false);
        if (offscreenLeftArrow != null) offscreenLeftArrow.gameObject.SetActive(false);
        if (offscreenRightArrow != null) offscreenRightArrow.gameObject.SetActive(false);
    }

    private RectTransform CreateArrow(string name, bool pointsLeft)
    {
        GameObject go = new GameObject(name);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(compassPanel, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(18f, 22f);
        rt.anchoredPosition = new Vector2(pointsLeft ? -halfCompassPixels : halfCompassPixels, 0f);

        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = pointsLeft ? "<" : ">";
        txt.fontSize = 22f;
        txt.fontStyle = FontStyles.Bold;
        txt.alignment = TextAlignmentOptions.Center;
        txt.color = new Color(1f, 0.85f, 0.4f);
        txt.raycastTarget = false;
        return rt;
    }

    private void LateUpdate()
    {
        if (mainCamera == null || compassMarks.Count == 0) return;

        float camAngle = mainCamera.eulerAngles.y;
        float fadeOuterStart = compassViewAngle * fullOpacityRange;

        for (int i = 0; i < compassMarks.Count; i++)
        {
            CompassMark mark = compassMarks[i];
            float markAngle = i * 15f;
            float angleDiff = Mathf.DeltaAngle(camAngle, markAngle);
            float absDiff = Mathf.Abs(angleDiff);

            bool visible = absDiff <= compassViewAngle;
            if (mark.rect.gameObject.activeSelf != visible) mark.rect.gameObject.SetActive(visible);
            if (!visible) continue;

            mark.rect.anchoredPosition = new Vector2(angleDiff * pixelsPerDegree, 0f);

            // Smooth fade as the mark approaches the edge of the compass view —
            // turns the rough hard-cull boundary into the soft falloff you see
            // in shipped AAA HUDs (Horizon, Cyberpunk, etc.).
            float edgeFade = absDiff <= fadeOuterStart
                ? 1f
                : 1f - Mathf.InverseLerp(fadeOuterStart, compassViewAngle, absDiff);

            Color c = mark.text.color;
            c.a = mark.baseAlpha * edgeFade;
            mark.text.color = c;

            // Subtle perspective: marks near the centre sit at full scale,
            // edges shrink ~10% so the compass feels rounded rather than flat.
            float perspective = 1f - (absDiff / compassViewAngle) * 0.1f;
            mark.rect.localScale = mark.baseScale * perspective;
        }

        UpdateMarkers(camAngle, fadeOuterStart);
    }

    private string GetCompassMark(int angle)
    {
        if (angle == 0) return "<color=#FF5555>N</color>";
        if (angle == 90) return "<color=#55FF55>E</color>";
        if (angle == 180) return "<color=#5588FF>S</color>";
        if (angle == 270) return "<color=#FFD55F>W</color>";
        if (angle % 30 == 0) return angle.ToString();
        return "·"; // minor tick — visually subtler than a number
    }

    private void UpdateMarkers(float camAngle, float fadeOuterStart)
    {
        if (markersParent == null || markerIconPrefab == null) return;

        activeMarkers.RemoveAll(item => item == null);

        // Grow icon pool if needed (and cache RectTransform / CanvasGroup /
        // distance label so we don't re-resolve them every frame per icon).
        int iconsNeeded = activeMarkers.Count - spawnedIcons.Count;
        for (int i = 0; i < iconsNeeded; i++) SpawnIcon();

        // Shrink: park excess icons into the pool (deactivated) instead of
        // destroying them. Markers come and go often (region totems,
        // quest objectives, distant landmarks) and Destroy + Instantiate
        // each transition was needless GC pressure.
        while (spawnedIcons.Count > activeMarkers.Count)
        {
            int last = spawnedIcons.Count - 1;
            GameObject g = spawnedIcons[last];
            if (g != null) g.SetActive(false);
            iconPool.Push(g);
            iconPoolRects.Push(spawnedIconRects[last]);
            iconPoolCanvas.Push(spawnedIconCanvas[last]);
            iconPoolDistance.Push(spawnedIconDistance[last]);
            spawnedIcons.RemoveAt(last);
            spawnedIconRects.RemoveAt(last);
            spawnedIconCanvas.RemoveAt(last);
            spawnedIconDistance.RemoveAt(last);
        }

        Transform distRef = distanceReference != null ? distanceReference : mainCamera;
        bool anyBehindLeft = false, anyBehindRight = false;

        for (int i = 0; i < activeMarkers.Count; i++)
        {
            Transform target = activeMarkers[i];
            Vector3 dirToTarget = target.position - mainCamera.position;
            float targetAngle = Mathf.Atan2(dirToTarget.x, dirToTarget.z) * Mathf.Rad2Deg;
            float angleDiff = Mathf.DeltaAngle(camAngle, targetAngle);
            float absDiff = Mathf.Abs(angleDiff);

            GameObject iconGO = spawnedIcons[i];
            RectTransform iconRect = spawnedIconRects[i];
            CanvasGroup cg = spawnedIconCanvas[i];
            TextMeshProUGUI distLabel = spawnedIconDistance[i];

            if (absDiff <= compassViewAngle)
            {
                if (!iconGO.activeSelf) iconGO.SetActive(true);
                iconRect.anchoredPosition = new Vector2(angleDiff * pixelsPerDegree, -20f);

                float edgeFade = absDiff <= fadeOuterStart
                    ? 1f
                    : 1f - Mathf.InverseLerp(fadeOuterStart, compassViewAngle, absDiff);
                if (cg != null) cg.alpha = edgeFade;

                if (distLabel != null)
                {
                    if (showDistanceOnIcons && distRef != null)
                    {
                        if (!distLabel.gameObject.activeSelf) distLabel.gameObject.SetActive(true);
                        Vector3 flatDelta = target.position - distRef.position;
                        flatDelta.y = 0f;
                        distLabel.text = Mathf.RoundToInt(flatDelta.magnitude) + "m";
                    }
                    else if (distLabel.gameObject.activeSelf) distLabel.gameObject.SetActive(false);
                }
            }
            else
            {
                if (iconGO.activeSelf) iconGO.SetActive(false);
                if (angleDiff < 0f) anyBehindLeft = true; else anyBehindRight = true;
            }
        }

        if (showOffScreenArrows)
        {
            if (offscreenLeftArrow != null && offscreenLeftArrow.gameObject.activeSelf != anyBehindLeft)
                offscreenLeftArrow.gameObject.SetActive(anyBehindLeft);
            if (offscreenRightArrow != null && offscreenRightArrow.gameObject.activeSelf != anyBehindRight)
                offscreenRightArrow.gameObject.SetActive(anyBehindRight);
        }
    }

    private void SpawnIcon()
    {
        GameObject icon;
        RectTransform iconRect;
        CanvasGroup cg;
        TextMeshProUGUI distLabel;

        if (iconPool.Count > 0)
        {
            icon = iconPool.Pop();
            iconRect = iconPoolRects.Pop();
            cg = iconPoolCanvas.Pop();
            distLabel = iconPoolDistance.Pop();
            if (icon != null) icon.SetActive(true);
            spawnedIcons.Add(icon);
            spawnedIconRects.Add(iconRect);
            spawnedIconCanvas.Add(cg);
            spawnedIconDistance.Add(distLabel);
            return;
        }

        icon = Instantiate(markerIconPrefab, markersParent, false);
        iconRect = icon.GetComponent<RectTransform>();
        iconRect.localScale = Vector3.one;

        cg = icon.GetComponent<CanvasGroup>();
        if (cg == null) cg = icon.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;

        distLabel = null;
        if (showDistanceOnIcons)
        {
            GameObject labelGO = new GameObject("Distance");
            RectTransform lRT = labelGO.AddComponent<RectTransform>();
            lRT.SetParent(iconRect, false);
            lRT.anchorMin = new Vector2(0.5f, 0f);
            lRT.anchorMax = new Vector2(0.5f, 0f);
            lRT.pivot = new Vector2(0.5f, 1f);
            lRT.anchoredPosition = new Vector2(0f, -2f);
            lRT.sizeDelta = new Vector2(60f, 16f);

            distLabel = labelGO.AddComponent<TextMeshProUGUI>();
            distLabel.fontSize = 11f;
            distLabel.alignment = TextAlignmentOptions.Center;
            distLabel.color = new Color(1f, 1f, 1f, 0.9f);
            distLabel.outlineWidth = 0.18f;
            distLabel.outlineColor = Color.black;
            distLabel.raycastTarget = false;
        }

        spawnedIcons.Add(icon);
        spawnedIconRects.Add(iconRect);
        spawnedIconCanvas.Add(cg);
        spawnedIconDistance.Add(distLabel);
    }
}
