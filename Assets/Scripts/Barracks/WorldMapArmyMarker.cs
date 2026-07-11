using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Spawns / drives the small figurines that walk across the world map while
// mercenary campaigns are in flight. Lives on the MapCanvas root.
//
// The marker is a UI RectTransform (Image) that we lerp along a straight
// line between the camp origin point and the region's own map rect. On
// resolve it stops for the "fighting" phase, then walks back.
public class WorldMapArmyMarker : MonoBehaviour
{
    [Header("Wiring")]
    // Point on the MapCanvas where armies visually depart from — usually
    // the camp icon on the map.
    public RectTransform armyOriginPoint;
    // Parent that new figurines are spawned into (typically the same as
    // armyOriginPoint's parent).
    public RectTransform markerParent;
    // Prefab: root RectTransform + Image (mapFigurineSprite is assigned per-unit-type).
    public GameObject figurinePrefab;

    // The map RegionUI components — used to look up a region's on-map
    // position by regionID. Auto-populated via FindObjectsByType at Start
    // if left empty.
    [Header("Region Node Lookup")]
    public List<RegionUI> regionNodes = new List<RegionUI>();

    // One marker per active campaign — keyed by campaignID.
    private readonly Dictionary<int, RectTransform> markers = new Dictionary<int, RectTransform>();

    private void Start()
    {
        if (regionNodes.Count == 0)
        {
            var found = FindObjectsByType<RegionUI>(FindObjectsSortMode.None);
            regionNodes.AddRange(found);
        }

        // Marker for campaigns that were already running when the scene loaded.
        if (MercenaryCampaignManager.Instance != null)
        {
            foreach (var c in MercenaryCampaignManager.Instance.ActiveCampaigns)
                EnsureMarker(c);
        }

        MercenaryCampaignManager.OnCampaignStarted += EnsureMarker;
        MercenaryCampaignManager.OnCampaignReturned += RemoveMarker;
    }

    private void OnDestroy()
    {
        MercenaryCampaignManager.OnCampaignStarted -= EnsureMarker;
        MercenaryCampaignManager.OnCampaignReturned -= RemoveMarker;
    }

    private void Update()
    {
        if (MercenaryCampaignManager.Instance == null) return;

        foreach (var c in MercenaryCampaignManager.Instance.ActiveCampaigns)
        {
            if (!markers.TryGetValue(c.campaignID, out var m) || m == null) { EnsureMarker(c); continue; }
            UpdateMarker(m, c);
        }
    }

    private void EnsureMarker(MercenaryCampaign c)
    {
        if (c == null) return;
        if (markers.ContainsKey(c.campaignID)) return;
        if (figurinePrefab == null || markerParent == null) return;

        var go = Instantiate(figurinePrefab, markerParent);
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        markers[c.campaignID] = rt;

        // Pick a figurine sprite from the first alive unit in the army.
        var img = go.GetComponent<Image>();
        if (img != null && MercenaryRoster.Instance != null)
        {
            foreach (var uid in c.armyUIDs)
            {
                var all = MercenaryRoster.Instance.GetAllUnits();
                foreach (var u in all)
                {
                    if (u.uid == uid)
                    {
                        var data = MercenaryRoster.Instance.GetData(u.unitID);
                        if (data != null && data.mapFigurineSprite != null) img.sprite = data.mapFigurineSprite;
                        break;
                    }
                }
                if (img.sprite != null) break;
            }
        }

        UpdateMarker(rt, c);
    }

    private void RemoveMarker(MercenaryCampaign c)
    {
        if (c == null) return;
        if (markers.TryGetValue(c.campaignID, out var rt))
        {
            if (rt != null) Destroy(rt.gameObject);
            markers.Remove(c.campaignID);
        }
    }

    private void UpdateMarker(RectTransform marker, MercenaryCampaign c)
    {
        var origin = armyOriginPoint;
        var target = FindRegionNodeRect(c.regionID);
        if (origin == null || target == null) return;

        Vector2 start = origin.anchoredPosition;
        Vector2 end = target.anchoredPosition;

        var phase = c.CurrentPhase();
        switch (phase)
        {
            case CampaignPhase.MarchingOut:
                marker.anchoredPosition = Vector2.Lerp(start, end, c.OutboundProgress01());
                break;
            case CampaignPhase.Fighting:
                marker.anchoredPosition = end;
                break;
            case CampaignPhase.Returning:
                marker.anchoredPosition = Vector2.Lerp(end, start, c.ReturnProgress01());
                break;
            case CampaignPhase.Done:
                marker.anchoredPosition = start;
                break;
        }

        // Face the direction of travel — for a UI icon we spin its Z axis
        // so a top-mounted flag pole leans forward.
        Vector2 dir;
        if (phase == CampaignPhase.MarchingOut) dir = (end - start);
        else if (phase == CampaignPhase.Returning) dir = (start - end);
        else dir = Vector2.zero;

        if (dir.sqrMagnitude > 0.0001f)
        {
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            marker.localRotation = Quaternion.Euler(0f, 0f, ang);
        }
    }

    private RectTransform FindRegionNodeRect(int regionID)
    {
        for (int i = 0; i < regionNodes.Count; i++)
        {
            var n = regionNodes[i];
            if (n != null && n.myRegionData != null && n.myRegionData.regionID == regionID)
                return n.GetComponent<RectTransform>();
        }
        return null;
    }
}
