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

    // One marker per active campaign — keyed by campaignID. `paths` stores
    // the waypoint polyline (origin → conquered stepping stones → target)
    // so the army visually detours around locked regions instead of
    // teleporting through them.
    private readonly Dictionary<int, RectTransform> markers = new Dictionary<int, RectTransform>();
    private readonly Dictionary<int, List<Vector2>> paths = new Dictionary<int, List<Vector2>>();
    private readonly Dictionary<int, float[]> pathCumLen = new Dictionary<int, float[]>();

    [Header("Motion Feel")]
    // Marching bob — small sin-wave vertical wobble so the figurine reads
    // as walking, not sliding.
    public float bobHeight = 6f;
    public float bobSpeed = 6f;
    // Random shake amplitude during the Fighting phase — sells the "battle
    // is happening here" beat.
    public float fightShake = 4f;

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

        BuildPath(c);
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
        paths.Remove(c.campaignID);
        pathCumLen.Remove(c.campaignID);
    }

    // BFS through neighbouringRegions to find a corridor of Conquered
    // regions from home to the target's nearest conquered neighbour, then
    // step onto the target itself. Falls back to a straight line if the
    // graph can't reach the target (e.g. a dev-forced-unlock leaves the
    // target isolated).
    private void BuildPath(MercenaryCampaign c)
    {
        if (c == null || armyOriginPoint == null) return;

        RegionUI targetNode = FindRegionNode(c.regionID);
        if (targetNode == null) return;

        List<Vector2> pts = new List<Vector2>();
        pts.Add(armyOriginPoint.anchoredPosition);

        // Find the shortest chain of Conquered neighbours leading to the
        // target. If no chain exists we still add the target as the final
        // waypoint — the marker will straight-line for one leg.
        List<RegionData> chain = FindConqueredChain(targetNode.myRegionData);
        if (chain != null)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                var node = FindRegionNode(chain[i].regionID);
                if (node != null)
                {
                    var rt = node.GetComponent<RectTransform>();
                    if (rt != null) pts.Add(rt.anchoredPosition);
                }
            }
        }

        // Always end at the target itself.
        var targetRT = targetNode.GetComponent<RectTransform>();
        if (targetRT != null) pts.Add(targetRT.anchoredPosition);

        paths[c.campaignID] = pts;

        // Precompute cumulative lengths so t → position lookup is O(log n).
        float[] cum = new float[pts.Count];
        cum[0] = 0f;
        for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
        pathCumLen[c.campaignID] = cum;
    }

    private List<RegionData> FindConqueredChain(RegionData target)
    {
        if (target == null) return null;
        // BFS from target back through neighbours; a neighbour is walkable
        // if it's Conquered. We stop as soon as we reach a Conquered region
        // whose distance from target is minimal — the path from that first-
        // seen conquered region back to target IS the chain.
        var visited = new HashSet<int>();
        var queue = new Queue<(RegionData region, List<RegionData> path)>();
        queue.Enqueue((target, new List<RegionData> { }));
        visited.Add(target.regionID);

        while (queue.Count > 0)
        {
            var (region, path) = queue.Dequeue();
            if (region.neighboringRegions == null) continue;
            foreach (var n in region.neighboringRegions)
            {
                if (n == null || visited.Contains(n.regionID)) continue;
                visited.Add(n.regionID);
                var next = new List<RegionData>(path);
                next.Insert(0, n);
                if (n.currentState == RegionState.Conquered)
                {
                    // Walk back through `next` — order is target-neighbour first.
                    return next;
                }
                queue.Enqueue((n, next));
                if (visited.Count > 64) return null; // sanity cap
            }
        }
        return null;
    }

    private RegionUI FindRegionNode(int regionID)
    {
        for (int i = 0; i < regionNodes.Count; i++)
        {
            var n = regionNodes[i];
            if (n != null && n.myRegionData != null && n.myRegionData.regionID == regionID) return n;
        }
        return null;
    }

    // Interpolate along the precomputed polyline. `t01` = 0 at path start,
    // 1 at path end. Returns the world-ish anchored position.
    private Vector2 SamplePath(int campaignID, float t01, out Vector2 tangent)
    {
        tangent = Vector2.right;
        if (!paths.TryGetValue(campaignID, out var pts) || pts == null || pts.Count == 0)
            return Vector2.zero;
        if (pts.Count == 1) return pts[0];

        float[] cum = pathCumLen[campaignID];
        float total = cum[cum.Length - 1];
        if (total < 0.001f) return pts[0];
        float d = Mathf.Clamp01(t01) * total;

        // Find segment containing d.
        int seg = 0;
        for (int i = 1; i < cum.Length; i++)
        {
            if (cum[i] >= d) { seg = i - 1; break; }
            seg = i - 1;
        }
        float segStart = cum[seg];
        float segLen = Mathf.Max(0.001f, cum[seg + 1] - segStart);
        float segT = (d - segStart) / segLen;
        Vector2 a = pts[seg], b = pts[seg + 1];
        tangent = (b - a).normalized;
        return Vector2.Lerp(a, b, segT);
    }

    private void UpdateMarker(RectTransform marker, MercenaryCampaign c)
    {
        var origin = armyOriginPoint;
        var target = FindRegionNodeRect(c.regionID);
        if (origin == null || target == null) return;

        // Build the path lazily — the map might not have been populated when
        // EnsureMarker first ran on scene load.
        if (!paths.ContainsKey(c.campaignID)) BuildPath(c);

        Vector2 basePos;
        Vector2 dirTangent = Vector2.zero;

        var phase = c.CurrentPhase();
        switch (phase)
        {
            case CampaignPhase.MarchingOut:
                basePos = SamplePath(c.campaignID, c.OutboundProgress01(), out dirTangent);
                break;
            case CampaignPhase.Fighting:
                basePos = SamplePath(c.campaignID, 1f, out dirTangent);
                break;
            case CampaignPhase.Returning:
                // Walk the polyline backwards on the return leg so the army
                // takes the same conquered corridor home.
                basePos = SamplePath(c.campaignID, 1f - c.ReturnProgress01(), out dirTangent);
                dirTangent = -dirTangent;
                break;
            default:
                basePos = origin.anchoredPosition;
                break;
        }

        // Layer per-phase motion on top of the base position for feel.
        Vector2 motion = Vector2.zero;
        if (phase == CampaignPhase.MarchingOut || phase == CampaignPhase.Returning)
        {
            // Sin-wave vertical bob — evoke walking without needing an anim clip.
            float t = Time.time * bobSpeed + c.campaignID * 0.7f; // phase offset per campaign
            motion.y = Mathf.Sin(t) * bobHeight;
        }
        else if (phase == CampaignPhase.Fighting)
        {
            // Frenetic tiny shake — signals clash. Random per-frame is fine
            // at this scale; the eye reads it as motion, not noise.
            motion.x = (Random.value - 0.5f) * fightShake * 2f;
            motion.y = (Random.value - 0.5f) * fightShake * 2f;
        }
        marker.anchoredPosition = basePos + motion;

        // Face the direction of travel using the path segment tangent so
        // corners of the polyline snap the marker to the new heading.
        if (dirTangent.sqrMagnitude > 0.0001f)
        {
            float ang = Mathf.Atan2(dirTangent.y, dirTangent.x) * Mathf.Rad2Deg - 90f;
            marker.localRotation = Quaternion.Euler(0f, 0f, ang);
        }

        // Optional per-figurine countdown — if the marker prefab has a
        // TMP labelled "TimerText" as a child, we update it with
        // total time remaining until the campaign resolves and clears.
        var timer = marker.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        if (timer != null)
        {
            float remain = Mathf.Max(0f, c.TotalPhaseDuration - c.SecondsSinceStart());
            int m = Mathf.FloorToInt(remain / 60f);
            int s = Mathf.FloorToInt(remain % 60f);
            timer.text = m > 0 ? $"{m}:{s:D2}" : $"0:{s:D2}";
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
