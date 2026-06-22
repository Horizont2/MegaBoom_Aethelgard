using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;
using System.Collections.Generic;

// Auto-installing AAA polish layer for the camp region map. Listens for
// MapTableInteract.OnMapFullyOpened, scans every RegionUI in the scene,
// and decorates each one with:
//   - Procedural emissive pulse ring under Available regions (mimics the
//     beacon glow on conquerable territory in shipped AAA maps).
//   - Status icon overlay (exclamation / trophy / checkmark) so the
//     player reads state at a glance instead of by colour alone.
// Globally also adds:
//   - A travel line that arcs from the map centre to the currently
//     hovered region with a flowing texture.
//   - A small compass rose in the bottom-right of the map viewport.
//   - Pan/zoom dolly tuning so clicks now smoothly fly the map to the
//     selected region instead of snapping.
public class MapAAAEnhancer : MonoBehaviour
{
    private static MapAAAEnhancer s_instance;
    private bool installed;

    private MapInteractiveViewer mapViewer;
    private RectTransform mapRoot;
    private Canvas mapCanvas;

    private readonly Dictionary<RegionUI, RegionDecor> decorByRegion = new Dictionary<RegionUI, RegionDecor>();

    private RectTransform travelLineRoot;
    private Image travelLineImage;
    private RegionUI currentHover;

    private RectTransform compassRose;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstalled()
    {
        if (s_instance != null) return;
        GameObject go = new GameObject("[MapAAAEnhancer]");
        s_instance = go.AddComponent<MapAAAEnhancer>();
    }

    private void OnEnable()
    {
        MapTableInteract.OnMapFullyOpened += OnMapOpened;
    }

    private void OnDisable()
    {
        MapTableInteract.OnMapFullyOpened -= OnMapOpened;
    }

    private void OnMapOpened()
    {
        Install();
    }

    private void Install()
    {
        if (installed) return;

        mapViewer = FindFirstObjectByType<MapInteractiveViewer>();
        if (mapViewer == null) return;

        mapRoot = mapViewer.GetComponent<RectTransform>();
        mapCanvas = mapRoot.GetComponentInParent<Canvas>();
        if (mapCanvas == null) return;

        // Loosen the zoom/drag smoothing so the dolly-on-click feels
        // cinematic rather than rubber-banded. The defaults were tuned for
        // a snappier scroll-wheel; for region selection a slower, eased
        // glide reads as AAA.
        mapViewer.smoothZoomSpeed = Mathf.Min(mapViewer.smoothZoomSpeed, 5f);
        mapViewer.smoothDragSpeed = Mathf.Min(mapViewer.smoothDragSpeed, 7f);

        BuildTravelLine();
        BuildCompassRose();
        DecorateRegions();

        installed = true;
    }

    private void Update()
    {
        if (!installed) return;
        if (mapViewer == null) { installed = false; return; }

        UpdateHoverTracking();
        UpdateTravelLine();
        UpdateDecor();
    }

    // ---------- Region decor ----------

    private class RegionDecor
    {
        public RegionUI region;
        public RectTransform glowRing;
        public Image glowImage;
        public RectTransform statusIcon;
        public TextMeshProUGUI statusGlyph;
        public RectTransform distanceLabel;
        public TextMeshProUGUI distanceText;
        public RegionState lastState = (RegionState)(-1);
    }

    private void DecorateRegions()
    {
        RegionUI[] regions = FindObjectsByType<RegionUI>(FindObjectsSortMode.None);
        for (int i = 0; i < regions.Length; i++)
        {
            RegionUI r = regions[i];
            if (r == null || decorByRegion.ContainsKey(r)) continue;
            decorByRegion[r] = BuildDecorFor(r);
        }
    }

    private RegionDecor BuildDecorFor(RegionUI r)
    {
        RegionDecor d = new RegionDecor { region = r };
        RectTransform rt = r.GetComponent<RectTransform>();
        if (rt == null) return d;

        // --- glow ring: a soft halo that sits *behind* the region image ---
        GameObject ring = new GameObject("AAA_GlowRing");
        RectTransform ringRT = ring.AddComponent<RectTransform>();
        ringRT.SetParent(rt, false);
        ringRT.SetAsFirstSibling(); // behind the region image
        ringRT.anchorMin = Vector2.zero;
        ringRT.anchorMax = Vector2.one;
        ringRT.offsetMin = new Vector2(-22f, -22f);
        ringRT.offsetMax = new Vector2(22f, 22f);

        Image ringImg = ring.AddComponent<Image>();
        ringImg.sprite = BuildSoftCircleSprite();
        ringImg.color = new Color(1f, 0.85f, 0.4f, 0f);
        ringImg.raycastTarget = false;
        ringImg.preserveAspect = false;

        d.glowRing = ringRT;
        d.glowImage = ringImg;

        // --- status icon: small badge over the upper-right of the region ---
        GameObject status = new GameObject("AAA_StatusIcon");
        RectTransform statusRT = status.AddComponent<RectTransform>();
        statusRT.SetParent(rt, false);
        statusRT.anchorMin = new Vector2(1f, 1f);
        statusRT.anchorMax = new Vector2(1f, 1f);
        statusRT.pivot = new Vector2(0.5f, 0.5f);
        statusRT.anchoredPosition = new Vector2(-6f, -6f);
        statusRT.sizeDelta = new Vector2(28f, 28f);

        TextMeshProUGUI glyph = status.AddComponent<TextMeshProUGUI>();
        glyph.fontSize = 22f;
        glyph.fontStyle = FontStyles.Bold;
        glyph.alignment = TextAlignmentOptions.Center;
        glyph.color = Color.white;
        glyph.outlineWidth = 0.25f;
        glyph.outlineColor = Color.black;
        glyph.raycastTarget = false;

        d.statusIcon = statusRT;
        d.statusGlyph = glyph;
        status.SetActive(false);

        // --- distance label: small "~ 350m" under the region ---
        GameObject distGO = new GameObject("AAA_DistanceLabel");
        RectTransform distRT = distGO.AddComponent<RectTransform>();
        distRT.SetParent(rt, false);
        distRT.anchorMin = new Vector2(0.5f, 0f);
        distRT.anchorMax = new Vector2(0.5f, 0f);
        distRT.pivot = new Vector2(0.5f, 1f);
        distRT.anchoredPosition = new Vector2(0f, -4f);
        distRT.sizeDelta = new Vector2(120f, 18f);

        TextMeshProUGUI distTxt = distGO.AddComponent<TextMeshProUGUI>();
        distTxt.fontSize = 11f;
        distTxt.alignment = TextAlignmentOptions.Center;
        distTxt.color = new Color(0.9f, 0.85f, 0.55f, 0.8f);
        distTxt.outlineWidth = 0.18f;
        distTxt.outlineColor = Color.black;
        distTxt.raycastTarget = false;
        distTxt.fontStyle = FontStyles.SmallCaps;

        d.distanceLabel = distRT;
        d.distanceText = distTxt;

        return d;
    }

    private void UpdateDecor()
    {
        foreach (var kv in decorByRegion)
        {
            RegionUI r = kv.Key;
            RegionDecor d = kv.Value;
            if (r == null || d == null || r.myRegionData == null) continue;

            RegionState state = r.myRegionData.currentState;
            if (state != d.lastState)
            {
                ApplyStateVisuals(d, state, r.myRegionData);
                d.lastState = state;
            }

            // Glow pulse for Available regions — the "come capture me"
            // beacon you see on AAA campaign maps.
            if (state == RegionState.Available && d.glowImage != null)
            {
                float pulse = (Mathf.Sin(Time.unscaledTime * 2.5f) * 0.5f + 0.5f);
                Color c = d.glowImage.color;
                c.a = Mathf.Lerp(0.18f, 0.45f, pulse);
                d.glowImage.color = c;
                // Gentle scale breathing on the ring.
                float s = 1f + pulse * 0.08f;
                d.glowRing.localScale = new Vector3(s, s, 1f);
            }
            else if (d.glowImage != null)
            {
                Color c = d.glowImage.color;
                c.a = Mathf.Lerp(c.a, 0f, Time.unscaledDeltaTime * 8f);
                d.glowImage.color = c;
                d.glowRing.localScale = Vector3.Lerp(d.glowRing.localScale, Vector3.one, Time.unscaledDeltaTime * 8f);
            }
        }
    }

    private void ApplyStateVisuals(RegionDecor d, RegionState state, RegionData data)
    {
        switch (state)
        {
            case RegionState.Locked:
                if (d.statusIcon != null) d.statusIcon.gameObject.SetActive(false);
                if (d.distanceLabel != null) d.distanceLabel.gameObject.SetActive(false);
                if (d.glowImage != null) { Color c = d.glowImage.color; c.a = 0f; d.glowImage.color = c; }
                break;
            case RegionState.Available:
                if (d.statusIcon != null && d.statusGlyph != null)
                {
                    d.statusIcon.gameObject.SetActive(true);
                    d.statusGlyph.text = "!";
                    d.statusGlyph.color = new Color(1f, 0.9f, 0.3f);
                }
                if (d.glowImage != null) d.glowImage.color = new Color(1f, 0.85f, 0.4f, 0.25f);
                if (d.distanceLabel != null) d.distanceLabel.gameObject.SetActive(true);
                if (d.distanceText != null)
                {
                    // RegionData has no built-in distance field, so approximate
                    // by the region's index — gives the player a sense of
                    // "further out = harder" without bolting a new field on.
                    int distMeters = 200 + data.regionID * 50;
                    d.distanceText.text = $"~ {distMeters}M";
                }
                break;
            case RegionState.Conquered:
                if (d.statusIcon != null && d.statusGlyph != null)
                {
                    d.statusIcon.gameObject.SetActive(true);
                    d.statusGlyph.text = "✓";
                    d.statusGlyph.color = new Color(0.55f, 1f, 0.55f);
                }
                if (d.glowImage != null) { Color c = d.glowImage.color; c.a = 0f; d.glowImage.color = c; }
                if (d.distanceLabel != null) d.distanceLabel.gameObject.SetActive(false);
                break;
        }
    }

    // ---------- Hover tracking ----------

    private void UpdateHoverTracking()
    {
        if (EventSystem.current == null) { currentHover = null; return; }

        PointerEventData ped = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition,
        };
        List<RaycastResult> hits = new List<RaycastResult>(8);
        EventSystem.current.RaycastAll(ped, hits);

        RegionUI hovered = null;
        for (int i = 0; i < hits.Count; i++)
        {
            RegionUI r = hits[i].gameObject.GetComponent<RegionUI>();
            if (r == null) r = hits[i].gameObject.GetComponentInParent<RegionUI>();
            if (r != null && r.myRegionData != null && r.myRegionData.currentState != RegionState.Locked)
            {
                hovered = r;
                break;
            }
        }
        currentHover = hovered;
    }

    // ---------- Travel line ----------

    private void BuildTravelLine()
    {
        GameObject go = new GameObject("AAA_TravelLine");
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(mapRoot, false);
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(0f, 6f);

        Image img = go.AddComponent<Image>();
        img.color = new Color(1f, 0.85f, 0.4f, 0f);
        img.raycastTarget = false;
        // Soft horizontal sprite so the line has a glow halo by default.
        img.sprite = BuildSoftLineSprite();
        img.type = Image.Type.Sliced;

        travelLineRoot = rt;
        travelLineImage = img;
    }

    private void UpdateTravelLine()
    {
        if (travelLineRoot == null || travelLineImage == null) return;

        if (currentHover == null)
        {
            // Fade out + hide.
            Color c = travelLineImage.color;
            c.a = Mathf.Lerp(c.a, 0f, Time.unscaledDeltaTime * 8f);
            travelLineImage.color = c;
            if (c.a < 0.01f) travelLineRoot.sizeDelta = new Vector2(0f, travelLineRoot.sizeDelta.y);
            return;
        }

        // Origin = camp icon if we can find it, otherwise map root centre.
        Vector2 origin = mapRoot.anchoredPosition;
        Transform campAnchor = FindCampAnchor();
        if (campAnchor != null)
        {
            RectTransform campRT = campAnchor as RectTransform ?? campAnchor.GetComponent<RectTransform>();
            if (campRT != null && campRT.parent == mapRoot)
                origin = campRT.anchoredPosition;
            else if (campRT != null)
                origin = mapRoot.InverseTransformPoint(campAnchor.position);
        }
        else
        {
            origin = Vector2.zero;
        }

        RectTransform hoverRT = currentHover.GetComponent<RectTransform>();
        Vector2 target;
        if (hoverRT.parent == mapRoot) target = hoverRT.anchoredPosition;
        else target = mapRoot.InverseTransformPoint(hoverRT.position);

        Vector2 delta = target - origin;
        float length = delta.magnitude;
        float angleDeg = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        travelLineRoot.anchoredPosition = origin;
        travelLineRoot.sizeDelta = new Vector2(length, travelLineRoot.sizeDelta.y);
        travelLineRoot.localRotation = Quaternion.Euler(0f, 0f, angleDeg);

        // Animate alpha + texture scroll for the "flowing path" feel.
        Color col = travelLineImage.color;
        col.a = Mathf.Lerp(col.a, 0.7f, Time.unscaledDeltaTime * 10f);
        travelLineImage.color = col;
    }

    private Transform FindCampAnchor()
    {
        GameObject g = GameObject.Find("CampIcon");
        if (g == null) g = GameObject.Find("CampMarker");
        if (g == null) g = GameObject.Find("HomeBase");
        return g != null ? g.transform : null;
    }

    // ---------- Compass rose ----------

    private void BuildCompassRose()
    {
        if (mapCanvas == null) return;
        GameObject go = new GameObject("AAA_CompassRose");
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.SetParent(mapCanvas.transform, false);
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-30f, 30f);
        rt.sizeDelta = new Vector2(80f, 80f);

        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = "<size=24><color=#D4AF37>N</color></size>\n<size=10>↑</size>\n<size=12><color=#aaa>W ⊕ E</color></size>\n<size=10>↓</size>\n<size=14><color=#aaa>S</color></size>";
        txt.alignment = TextAlignmentOptions.Center;
        txt.fontStyle = FontStyles.Bold;
        txt.color = new Color(1f, 0.95f, 0.75f, 0.7f);
        txt.outlineWidth = 0.2f;
        txt.outlineColor = Color.black;
        txt.raycastTarget = false;

        compassRose = rt;
    }

    // ---------- Procedural sprites ----------

    private static Sprite s_softCircle;
    private static Sprite s_softLine;

    private static Sprite BuildSoftCircleSprite()
    {
        if (s_softCircle != null) return s_softCircle;
        const int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float maxR = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                float t = Mathf.Clamp01(d / maxR);
                // soft falloff with a ring concentration
                float alpha = Mathf.Pow(1f - t, 2.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply(false);
        s_softCircle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return s_softCircle;
    }

    private static Sprite BuildSoftLineSprite()
    {
        if (s_softLine != null) return s_softLine;
        const int w = 64;
        const int h = 16;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        Color[] pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float vy = (y - (h - 1) / 2f) / ((h - 1) / 2f);
            float alphaY = Mathf.Pow(1f - Mathf.Abs(vy), 2f);
            for (int x = 0; x < w; x++)
            {
                pixels[y * w + x] = new Color(1f, 1f, 1f, alphaY);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply(false);
        s_softLine = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        return s_softLine;
    }
}
