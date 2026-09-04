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
    // Route-line dashes per campaign — destroyed alongside the marker.
    private readonly Dictionary<int, List<Image>> routeDashes = new Dictionary<int, List<Image>>();
    // Dedicated layer for the route line. Sits ABOVE the map art but
    // BELOW the figurines. SetSiblingIndex(0) on individual dashes put
    // them behind the map background image (also a child of markerParent),
    // which is why the route never showed.
    private RectTransform routeLayer;
    // Smoothing state for the marker's position + heading between frames.
    private readonly Dictionary<int, Vector2> smoothedPos = new Dictionary<int, Vector2>();
    private readonly Dictionary<int, Vector2> smoothVel = new Dictionary<int, Vector2>();
    private readonly Dictionary<int, Quaternion> smoothedRot = new Dictionary<int, Quaternion>();
    // Track the last phase we drew per marker so we can trigger a one-shot
    // "just arrived" scale pulse when the phase transitions to Fighting.
    private readonly Dictionary<int, CampaignPhase> lastPhaseSeen = new Dictionary<int, CampaignPhase>();
    // Cached per-marker countdown TMP — GetComponentInChildren per frame
    // per marker was the map screen's biggest self-inflicted cost.
    private readonly Dictionary<int, TMPro.TextMeshProUGUI> markerTimers = new Dictionary<int, TMPro.TextMeshProUGUI>();

    [Header("Motion Feel")]
    // Marching bob — small sin-wave vertical wobble so the figurine reads
    // as walking, not sliding.
    public float bobHeight = 6f;
    public float bobSpeed = 6f;
    // Random shake amplitude during the Fighting phase — sells the "battle
    // is happening here" beat.
    public float fightShake = 4f;
    // Frame-to-frame smoothing for both position + rotation so the marker
    // eases between polyline segments instead of snapping.
    [Range(0f, 1f)] public float positionSmoothTime = 0.10f;
    [Range(0f, 20f)] public float rotationSlerpSpeed = 10f;

    [Tooltip("Turn the marker to face its direction of travel. Right for a figurine that has a front; wrong for a map PIN, which should stay upright — leave off for pin-style art.")]
    public bool rotateToHeading = false;

    [Tooltip("Keep the countdown text upright even when the marker rotates. Without this the timer tips over with the icon and stops being readable.")]
    public bool keepTimerUpright = true;

    [Header("Visible Route Line")]
    // Optional — assign a small UI dash prefab (RectTransform + Image, ~4×2px)
    // to draw a dotted route from origin along the polyline to the target.
    // Left null → no route line, only the marker moves.
    public GameObject routeDashPrefab;
    public float dashSpacing = 18f;
    // Colour applied to every dash on the outbound leg. Return leg uses
    // the same sprite tinted `routeReturnColor` so the player can tell
    // 'I'm coming back' at a glance.
    public Color routeOutboundColor = new Color(1f, 0.85f, 0.55f, 0.85f);
    public Color routeReturnColor   = new Color(0.65f, 0.85f, 1f, 0.6f);
    // Traveling dashes: after Fighting, dashes ahead of the marker fade to
    // 0 alpha; behind the marker they stay lit. Sells 'this bit is done'.
    public bool dashesFadeBehindMarker = true;

    private void Start()
    {
        if (regionNodes.Count == 0)
        {
            var found = FindObjectsByType<RegionUI>(FindObjectsSortMode.None);
            regionNodes.AddRange(found);
        }

        // Auto-fallback: if the designer didn't wire a routeDashPrefab,
        // build a simple one on the fly so the route line actually shows.
        // The prefab is a tiny UI Image (8×3px, oval-ish white). Users who
        // want a custom look can wire their own — this just guarantees the
        // feature is visible with no extra setup.
        if (routeDashPrefab == null)
        {
            var dashGO = new GameObject("AutoRouteDash");
            dashGO.SetActive(false); // template, never activated as-is
            var img = dashGO.AddComponent<Image>();
            img.raycastTarget = false;
            img.color = Color.white;
            var drt = dashGO.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0.5f, 0.5f);
            drt.anchorMax = new Vector2(0.5f, 0.5f);
            drt.pivot = new Vector2(0.5f, 0.5f);
            drt.sizeDelta = new Vector2(10f, 3f);
            // Park under this component so Destroy on scene teardown wipes it.
            dashGO.transform.SetParent(transform, false);
            routeDashPrefab = dashGO;
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

        ApplyArmySprite(go, c);

        // Cache the countdown TMP once per marker — looked up per frame before.
        markerTimers[c.campaignID] = go.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);

        BuildPath(c);
        UpdateMarker(rt, c);
    }

    // The figurine used to take the sprite of the FIRST unit it happened to
    // find, and its loop then broke on `img.sprite != null` — which is true from
    // the prefab's own sprite before anything is assigned, so for a mixed army
    // the icon was effectively arbitrary. It now picks the sprite of the most
    // numerous unit type, which is what the army actually reads as.
    //
    // It also no longer silently clobbers the prefab's sprite: if no unit type
    // supplies one, whatever the artist put on the prefab stays.
    private void ApplyArmySprite(GameObject go, MercenaryCampaign c)
    {
        var img = go.GetComponent<Image>();
        if (img == null || MercenaryRoster.Instance == null || c.armyUIDs == null) return;

        var counts = new Dictionary<Sprite, int>();
        var all = MercenaryRoster.Instance.GetAllUnits();
        foreach (var uid in c.armyUIDs)
        {
            foreach (var u in all)
            {
                if (u.uid != uid) continue;
                var data = MercenaryRoster.Instance.GetData(u.unitID);
                if (data != null && data.mapFigurineSprite != null)
                {
                    counts.TryGetValue(data.mapFigurineSprite, out int n);
                    counts[data.mapFigurineSprite] = n + 1;
                }
                break;
            }
        }

        Sprite best = null; int bestCount = 0;
        foreach (var kv in counts)
            if (kv.Value > bestCount) { best = kv.Key; bestCount = kv.Value; }

        if (best != null) img.sprite = best;
    }

    private void RemoveMarker(MercenaryCampaign c)
    {
        if (c == null) return;
        markerTimers.Remove(c.campaignID);
        lastTimerSecond.Remove(c.campaignID);
        if (markers.TryGetValue(c.campaignID, out var rt))
        {
            if (rt != null) Destroy(rt.gameObject);
            markers.Remove(c.campaignID);
        }
        if (routeDashes.TryGetValue(c.campaignID, out var dashes))
        {
            foreach (var d in dashes) if (d != null) Destroy(d.gameObject);
            routeDashes.Remove(c.campaignID);
        }
        paths.Remove(c.campaignID);
        pathCumLen.Remove(c.campaignID);
        smoothedPos.Remove(c.campaignID);
        smoothVel.Remove(c.campaignID);
        smoothedRot.Remove(c.campaignID);
    }

    // Spawn one Image per dash along the polyline, oriented to match its
    // segment. Called after BuildPath.
    private RectTransform GetRouteLayer()
    {
        if (routeLayer != null) return routeLayer;
        var go = new GameObject("RouteDashLayer", typeof(RectTransform));
        routeLayer = go.GetComponent<RectTransform>();
        routeLayer.SetParent(markerParent, false);
        // Stretch to fill the parent so children's anchoredPositions stay
        // in the same coordinate space the figurines use.
        routeLayer.anchorMin = Vector2.zero;
        routeLayer.anchorMax = Vector2.one;
        routeLayer.offsetMin = Vector2.zero;
        routeLayer.offsetMax = Vector2.zero;
        // Last sibling = on top of the map art. Figurines are re-raised
        // above it right after (see EnsureMarker / SpawnRouteDashes).
        routeLayer.SetAsLastSibling();
        return routeLayer;
    }

    private void SpawnRouteDashes(MercenaryCampaign c)
    {
        if (routeDashPrefab == null || markerParent == null) return;
        if (!paths.TryGetValue(c.campaignID, out var pts) || pts == null || pts.Count < 2) return;

        // Clean up any prior dashes for this campaign (path rebuilt).
        if (routeDashes.TryGetValue(c.campaignID, out var prev))
        {
            foreach (var d in prev) if (d != null) Destroy(d.gameObject);
        }
        var dashes = new List<Image>();

        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector2 a = pts[i], b = pts[i + 1];
            Vector2 delta = b - a;
            float segLen = delta.magnitude;
            if (segLen < 0.5f) continue;
            Vector2 dir = delta / segLen;
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            int count = Mathf.Max(1, Mathf.FloorToInt(segLen / dashSpacing));
            for (int k = 0; k < count; k++)
            {
                float t = (k + 0.5f) / count;
                Vector2 pos = Vector2.Lerp(a, b, t);
                // Spawn into the dedicated route layer (above the map art,
                // below the figurines) — NOT into markerParent at sibling
                // index 0, which parked dashes behind the map background.
                var go = Instantiate(routeDashPrefab, GetRouteLayer());
                // FORCE active — the auto-fallback prefab lives inactive
                // (as a template) so its clones inherit inactive state
                // unless we re-enable them here.
                go.SetActive(true);
                var drt = go.GetComponent<RectTransform>();
                if (drt == null) drt = go.AddComponent<RectTransform>();
                drt.anchoredPosition = pos;
                drt.localRotation = Quaternion.Euler(0f, 0f, ang);
                drt.localScale = Vector3.one;
                var img = go.GetComponent<Image>();
                if (img != null)
                {
                    img.color = routeOutboundColor;
                    img.enabled = true;
                    dashes.Add(img);
                }
            }
        }
        routeDashes[c.campaignID] = dashes;

        // Re-raise every figurine above the route layer so the army icon
        // always draws on top of its own trail.
        foreach (var kv in markers)
        {
            if (kv.Value != null) kv.Value.SetAsLastSibling();
        }

        GameLog.Info($"[WorldMapArmyMarker] Route line: {dashes.Count} dashes spawned for campaign {c.campaignID} (path pts={pts.Count}).");
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

        SpawnRouteDashes(c);
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
        // Trigger a one-shot arrival pulse the frame we transition into
        // Fighting — visual feedback for 'the army just made it there'.
        if (lastPhaseSeen.TryGetValue(c.campaignID, out var prevPhase))
        {
            if (prevPhase != CampaignPhase.Fighting && phase == CampaignPhase.Fighting)
                StartCoroutine(ArrivalPulseRoutine(marker));
        }
        lastPhaseSeen[c.campaignID] = phase;

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
            // Clash rattle. Per-frame Random re-rolled every frame, which reads
            // as static and is partly eaten by the SmoothDamp below; driving it
            // from time gives a shake that survives smoothing and stays legible
            // at any frame rate.
            float ft = Time.time * 26f + c.campaignID * 3.1f;
            motion.x = (Mathf.PerlinNoise(ft, 0.3f) - 0.5f) * fightShake * 2f;
            motion.y = (Mathf.PerlinNoise(0.7f, ft) - 0.5f) * fightShake * 2f;
        }

        // Smooth position between frames so the marker doesn't jitter when
        // it crosses a polyline corner. SmoothDamp is stable at variable
        // framerate and doesn't overshoot.
        Vector2 desired = basePos + motion;
        Vector2 current = smoothedPos.TryGetValue(c.campaignID, out var prev) ? prev : desired;
        Vector2 vel = smoothVel.TryGetValue(c.campaignID, out var v) ? v : Vector2.zero;
        Vector2 next = Vector2.SmoothDamp(current, desired, ref vel, positionSmoothTime);
        smoothedPos[c.campaignID] = next;
        smoothVel[c.campaignID] = vel;
        marker.anchoredPosition = next;

        // Smooth rotation so 90° polyline corners aren't a snap.
        if (rotateToHeading && dirTangent.sqrMagnitude > 0.0001f)
        {
            float ang = Mathf.Atan2(dirTangent.y, dirTangent.x) * Mathf.Rad2Deg - 90f;
            Quaternion targetRot = Quaternion.Euler(0f, 0f, ang);
            Quaternion currentRot = smoothedRot.TryGetValue(c.campaignID, out var q) ? q : targetRot;
            Quaternion slerped = Quaternion.Slerp(currentRot, targetRot, 1f - Mathf.Exp(-rotationSlerpSpeed * Time.deltaTime));
            smoothedRot[c.campaignID] = slerped;
            marker.localRotation = slerped;
        }
        else if (!rotateToHeading)
        {
            marker.localRotation = Quaternion.identity;
        }

        // Route-dash pass — recolour to match phase + fade behind marker.
        if (routeDashes.TryGetValue(c.campaignID, out var dashes) && dashes != null && dashes.Count > 0)
        {
            bool returning = phase == CampaignPhase.Returning;
            Color baseCol = returning ? routeReturnColor : routeOutboundColor;
            float progress01 = phase == CampaignPhase.MarchingOut
                             ? c.OutboundProgress01()
                             : (phase == CampaignPhase.Fighting ? 1f
                                 : (returning ? 1f - c.ReturnProgress01() : 0f));

            for (int i = 0; i < dashes.Count; i++)
            {
                var d = dashes[i];
                if (d == null) continue;
                float dashPos = dashes.Count > 1 ? (float)i / (dashes.Count - 1) : 0f;
                float a = baseCol.a;
                if (dashesFadeBehindMarker && phase != CampaignPhase.Done)
                {
                    // Ahead of marker → dim (0.35× alpha), behind → full.
                    a *= dashPos <= progress01 ? 1f : 0.35f;
                }
                d.color = new Color(baseCol.r, baseCol.g, baseCol.b, a);
            }
        }

        // Optional per-figurine countdown — cached TMP (looked up once in
        // EnsureMarker), refreshed only when the displayed second changes
        // so we don't re-layout TMP geometry 60×/s.
        if (markerTimers.TryGetValue(c.campaignID, out var timer) && timer != null)
        {
            float remain = Mathf.Max(0f, c.TotalPhaseDuration - c.SecondsSinceStart());
            int totalSeconds = Mathf.FloorToInt(remain);
            // Counter-rotate so the countdown stays readable even when the icon
            // is turned to face its heading.
            if (keepTimerUpright && rotateToHeading)
                timer.transform.rotation = Quaternion.identity;

            if (!lastTimerSecond.TryGetValue(c.campaignID, out int prevSec) || prevSec != totalSeconds)
            {
                lastTimerSecond[c.campaignID] = totalSeconds;
                int m = totalSeconds / 60;
                int s = totalSeconds % 60;
                timer.text = $"{m}:{s:D2}";
            }
        }
    }

    // Last whole-second value written to each marker's timer TMP.
    private readonly Dictionary<int, int> lastTimerSecond = new Dictionary<int, int>();

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

    // One-shot bounce on arrival at the target region. 1× → 1.35× → 1×
    // over 0.45s. Purely visual — 'the army is here'.
    private System.Collections.IEnumerator ArrivalPulseRoutine(RectTransform marker)
    {
        if (marker == null) yield break;
        Vector3 baseScale = marker.localScale;
        Vector3 peakScale = baseScale * 1.35f;
        float t = 0f;
        float up = 0.15f;
        while (t < up)
        {
            if (marker == null) yield break;
            t += Time.deltaTime;
            marker.localScale = Vector3.Lerp(baseScale, peakScale, Mathf.SmoothStep(0f, 1f, t / up));
            yield return null;
        }
        float down = 0.30f; t = 0f;
        while (t < down)
        {
            if (marker == null) yield break;
            t += Time.deltaTime;
            marker.localScale = Vector3.Lerp(peakScale, baseScale, Mathf.SmoothStep(0f, 1f, t / down));
            yield return null;
        }
        if (marker != null) marker.localScale = baseScale;
    }
}
