using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using TMPro;

// Post-tutorial camp onboarding guide. Playtesters landing in the camp
// scene after the tutorial reported being lost — this director sits above
// the mission board plate, always tells the player the ONE next thing
// they should do, and (optionally) draws a floor waypoint / line-renderer
// trail toward its target.
//
// The step list is driven off PlayerPrefs so it works with the game's
// existing save flags (Elias_Intro, SaveBld_ScoutsLodge, TotalConqueredRegions,
// etc.). Add steps by dragging waypoint Transforms + typing prompt keys —
// no code changes needed for new steps.
public class CampGuideDirector : MonoBehaviour
{
    [System.Serializable]
    public class GuideStep
    {
        [Tooltip("Short label shown to the player, e.g. 'Talk to Elias'. Passed through Tr.")]
        public string promptKey = "GUIDE_TALK_ELIAS";

        [Tooltip("Where to point. LineRenderer + waypoint marker follow this transform.")]
        public Transform target;

        [Header("Completion condition (any true → step done)")]
        [Tooltip("PlayerPrefs int key. Step is completed when this key's value ≥ requiredValue.")]
        public string playerPrefsKey;
        public int requiredValue = 1;
    }

    [Header("Steps (top-to-bottom priority)")]
    public List<GuideStep> steps = new List<GuideStep>();

    [Header("HUD Widgets (all optional)")]
    // TMP field on your camp HUD that shows the current step's prompt.
    public TextMeshProUGUI promptText;
    // The tutorial-style mission plate (same MissionUIElement the Lvl_1
    // quest chain uses). Drop the tutorial plate prefab onto the camp
    // canvas and wire it here — or leave null and the director grabs the
    // first MissionUIElement it finds in the scene. Each guide step shows
    // as a mission: strike-through + flash on completion, then the next
    // step slides in.
    public MissionUIElement missionPlate;
    // Floating world-space marker prefab dropped over the current target
    // (usually a beam-of-light or arrow). Instantiated once, moved between
    // targets as steps advance.
    public GameObject waypointMarkerPrefab;
    public float markerYOffset = 3f;

    [Header("Trail Line (optional)")]
    // Attach a LineRenderer here — the guide will animate it from player
    // to the current target across the NavMesh. Leave null → no line.
    public LineRenderer trailLine;
    [Tooltip("How often (seconds) to re-path the trail. Lower = the ribbon tracks the player more tightly. The per-frame ease keeps it smooth between recomputes.")]
    public float trailUpdateInterval = 0.12f;
    [Tooltip("Max samples along the NavMesh path — controls trail smoothness.")]
    public int trailMaxCorners = 32;

    [Header("Polling")]
    public float progressCheckInterval = 1.0f;

    private GameObject waypointMarker;
    private Transform player;
    private int currentStepIndex = -1;
    private float progressTimer = 0f;
    private float trailTimer = 0f;
    // Created in Awake — Unity throws if NavMeshPath is constructed in a
    // field initializer ("InitializeNavMeshPath is not allowed to be
    // called from a MonoBehaviour constructor"), which killed the whole
    // component before Start could run.
    private NavMeshPath scratchPath;

    private void Awake()
    {
        scratchPath = new NavMeshPath();
    }

    private void Start()
    {
        var pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null) player = pObj.transform;

        // Zero-config mode: if the designer didn't author any steps,
        // build the canonical onboarding chain by discovering targets in
        // the scene. Steps whose target can't be found are still added
        // (prompt-only, no marker) so the chain doesn't silently skip
        // progression beats.
        if (steps == null || steps.Count == 0) BuildDefaultSteps();

        // Auto-grab a mission plate if none is wired — lets the designer
        // just drop the tutorial plate prefab anywhere on the camp canvas.
        if (missionPlate == null)
            missionPlate = FindFirstObjectByType<MissionUIElement>(FindObjectsInactive.Include);

        if (waypointMarkerPrefab != null)
        {
            waypointMarker = Instantiate(waypointMarkerPrefab);
            waypointMarker.SetActive(false);
        }

        // Auto-create the trail LineRenderer when none is wired — a soft
        // gold ribbon along the NavMesh path. Wire your own for a custom
        // look; this fallback just guarantees the feature works.
        if (trailLine == null)
        {
            var go = new GameObject("GuideTrailLine");
            go.transform.SetParent(transform, false);
            trailLine = go.AddComponent<LineRenderer>();
            trailLine.widthMultiplier = 0.25f;
            trailLine.numCornerVertices = 4;
            trailLine.numCapVertices = 4;
            trailLine.textureMode = LineTextureMode.Tile;
            trailLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trailLine.receiveShadows = false;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            Color gold = new Color(1f, 0.82f, 0.4f, 0.55f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", gold);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", gold);
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f); // transparent
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            trailLine.material = mat;
            trailLine.enabled = false;
        }

        RecomputeCurrentStep();
        RefreshUI();
        RefreshPlate(initial: true);
    }

    // Drives the tutorial-style mission plate from the current guide step.
    private void RefreshPlate(bool initial = false)
    {
        if (missionPlate == null) return;

        if (currentStepIndex >= 0 && currentStepIndex < steps.Count)
        {
            if (!missionPlate.gameObject.activeSelf) missionPlate.gameObject.SetActive(true);
            if (initial) missionPlate.animateAppearance = true; // slide-in on first show
            missionPlate.Setup(
                LocalizationManager.Tr("GUIDE_PLATE_TITLE"),
                LocalizationManager.Tr(steps[currentStepIndex].promptKey),
                0, 1);
        }
        else
        {
            // All steps done — plate hides for good.
            missionPlate.gameObject.SetActive(false);
        }
    }

    // Strike-through + flash the finished step, hold a beat so the player
    // reads the completion, then slide the next objective in.
    private System.Collections.IEnumerator PlateAdvanceRoutine()
    {
        if (missionPlate != null && missionPlate.gameObject.activeSelf)
        {
            missionPlate.CompleteMission();
            yield return new WaitForSeconds(1.4f);
        }
        RefreshPlate();
    }

    // The canonical post-tutorial camp flow, driven off the same
    // PlayerPrefs flags Elias / buildings / map already write. Discovery
    // by component type — no Inspector wiring needed.
    private void BuildDefaultSteps()
    {
        steps = new List<GuideStep>();

        Transform eliasT = null;
        var elias = FindFirstObjectByType<CampNPC_Elias>();
        if (elias != null) eliasT = elias.transform;

        Transform mapT = null;
        var mapTable = FindFirstObjectByType<MapTableInteract>();
        if (mapTable != null) mapT = mapTable.transform;

        Transform barracksT = null;
        string barracksKey = "SaveBld_Barracks";
        var barracks = FindFirstObjectByType<BarracksBuilding>();
        if (barracks != null)
        {
            barracksT = barracks.transform;
            // Same save-key-derivation fix as storage: the companion
            // CampBuilding's real buildingID drives the key so the step
            // credits even if the ID isn't literally "Barracks".
            var barracksCB = barracks.GetComponent<CampBuilding>();
            if (barracksCB != null && !string.IsNullOrEmpty(barracksCB.buildingID))
                barracksKey = "SaveBld_" + barracksCB.buildingID;
        }

        // Distinct targets per step — the earlier version pointed BOTH the
        // "talk to Elias" and "upgrade lodge" steps at Elias, so after the
        // first conversation the trail still led back to him instead of the
        // lodge. Resolve the lodge by buildingID first, then by a name
        // containing "lodge". Deliberately NO Elias fallback — pointing the
        // "upgrade the lodge" trail at Elias is exactly the bug we're
        // fixing. Null target → the trail simply hides for that step.
        Transform lodgeT = FindBuildingTransform("ScoutsLodge");
        if (lodgeT == null) lodgeT = FindBuildingByName("lodge");
        if (lodgeT == null)
            Debug.LogWarning("[CampGuideDirector] Couldn't find the Scout's Lodge building (no CampBuilding with buildingID 'ScoutsLodge' or a name containing 'lodge'). The 'upgrade lodge' step will have no trail. Set the building's buildingID to 'ScoutsLodge'.");

        // Additional targets for the extended step chain.
        // Resolve the storage building as a CampBuilding so we can derive
        // its ACTUAL save key ("SaveBld_" + buildingID). Hardcoding
        // "SaveBld_StorageVault" broke the step whenever the building's
        // buildingID wasn't literally "StorageVault" (it was found via
        // the fuzzy name fallback, but the key check never matched its
        // real save key, so the step never credited).
        CampBuilding storageB = FindBuilding("StorageVault", "storage", "vault");
        Transform storageT = storageB != null ? storageB.transform : null;
        string storageKey = (storageB != null && !string.IsNullOrEmpty(storageB.buildingID))
            ? "SaveBld_" + storageB.buildingID
            : "SaveBld_StorageVault";
        Transform noticeT = FindNoticeBoard();
        Transform shopT = FindShop();

        // Full onboarding chain, ordered by natural progression. Each step
        // has its own PlayerPrefs key — see the game's existing save
        // conventions. Steps with a null target quietly fall back to a
        // prompt-only guide (trail hides, plate still shows the text).
        // 1. Meet Elias
        steps.Add(new GuideStep { promptKey = "GUIDE_TALK_ELIAS",     target = eliasT,    playerPrefsKey = "Elias_Intro",           requiredValue = 1 });
        // 2. Upgrade Scout's Lodge to unlock the map
        steps.Add(new GuideStep { promptKey = "GUIDE_BUILD_LODGE",    target = lodgeT,    playerPrefsKey = "SaveBld_ScoutsLodge",   requiredValue = 2 });
        // 3. Open the world map
        steps.Add(new GuideStep { promptKey = "GUIDE_USE_MAP_TABLE",  target = mapT,      playerPrefsKey = "MapOpenedOnce",         requiredValue = 1 });
        // 4. Conquer the first (hand-built) region — R1 Old Lumberyard
        steps.Add(new GuideStep { promptKey = "GUIDE_CONQUER_FIRST",  target = mapT,      playerPrefsKey = "TotalConqueredRegions", requiredValue = 1 });
        // 5. Build the storage vault so more resource capacity unlocks.
        //    Key derived from the real building above, not hardcoded.
        steps.Add(new GuideStep { promptKey = "GUIDE_BUILD_STORAGE",  target = storageT,  playerPrefsKey = storageKey,  requiredValue = 1 });
        // 6. Check the notice board for daily missions (extra income)
        steps.Add(new GuideStep { promptKey = "GUIDE_NOTICE_BOARD",   target = noticeT,   playerPrefsKey = "HasInteractedWithBoard", requiredValue = 1 });
        // 7. Build the barracks — unlocks the mercenary system.
        //    Key derived from the real building above, not hardcoded.
        steps.Add(new GuideStep { promptKey = "GUIDE_BUILD_BARRACKS", target = barracksT, playerPrefsKey = barracksKey,      requiredValue = 1 });
        // 8. Hire your first mercenary (any type — Roster.CountAliveTotal>=1)
        steps.Add(new GuideStep { promptKey = "GUIDE_HIRE_MERC",      target = barracksT, playerPrefsKey = "MercFirstHired",        requiredValue = 1 });
        // 9. Send an army to an auto-battle region
        steps.Add(new GuideStep { promptKey = "GUIDE_SEND_ARMY",      target = mapT,      playerPrefsKey = "MercFirstDeployed",     requiredValue = 1 });
        // 10. Visit the Shop and spend a diamond on gear
        steps.Add(new GuideStep { promptKey = "GUIDE_VISIT_SHOP",     target = shopT,     playerPrefsKey = "ShopFirstPurchase",     requiredValue = 1 });
        // 11. Reach the mid-game location (R8 Sunken Outpost — 3rd hand-built)
        steps.Add(new GuideStep { promptKey = "GUIDE_MIDGAME_REGION", target = mapT,      playerPrefsKey = "TotalConqueredRegions", requiredValue = 8 });
        // 12. Reach the city (R22 — pre-final hand-built)
        steps.Add(new GuideStep { promptKey = "GUIDE_REACH_CITY",     target = mapT,      playerPrefsKey = "TotalConqueredRegions", requiredValue = 21 });
        // 13. Final push: the Throne (R24)
        steps.Add(new GuideStep { promptKey = "GUIDE_FINAL_PUSH",     target = mapT,      playerPrefsKey = "TotalConqueredRegions", requiredValue = 24 });
    }

    // Notice-board / shop lookups by component-type-or-name. Keeps the
    // guide zero-config: the designer just drops the singleton on the
    // scene, no wiring needed.
    private Transform FindNoticeBoard()
    {
        var nb = FindFirstObjectByType<NoticeBoardManager>();
        return nb != null ? nb.transform : null;
    }
    private Transform FindShop()
    {
        // The shop lives in a separate scene — but its portal in camp is
        // usually named "Shop*" or has an ExtractionPortal targeting
        // ShopScene. Fall back to any GameObject named "Shop".
        foreach (var ep in FindObjectsByType<ExtractionPortal>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (ep != null && ep.campSceneName != null && ep.campSceneName.ToLowerInvariant().Contains("shop"))
                return ep.transform;
        }
        var go = GameObject.Find("Shop") ?? GameObject.Find("ShopPortal") ?? GameObject.Find("ShopInteract");
        return go != null ? go.transform : null;
    }

    private Transform FindBuildingTransform(string buildingID)
    {
        foreach (var b in FindObjectsByType<CampBuilding>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (b != null && b.buildingID == buildingID) return b.transform;
        }
        return null;
    }

    // Returns the CampBuilding (not just its Transform) matching an
    // exact buildingID first, then any of the fuzzy name/ID fragments.
    // Lets callers read the real buildingID for save-key derivation.
    private CampBuilding FindBuilding(string exactId, params string[] nameFragments)
    {
        var all = FindObjectsByType<CampBuilding>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var b in all)
            if (b != null && b.buildingID == exactId) return b;
        foreach (var b in all)
        {
            if (b == null) continue;
            string nm = b.name.ToLowerInvariant();
            string id = (b.buildingID ?? "").ToLowerInvariant();
            string dn = (b.buildingName ?? "").ToLowerInvariant();
            foreach (var frag in nameFragments)
            {
                string f = frag.ToLowerInvariant();
                if (nm.Contains(f) || id.Contains(f) || dn.Contains(f)) return b;
            }
        }
        return null;
    }

    private Transform FindBuildingByName(string nameFragment)
    {
        string frag = nameFragment.ToLowerInvariant();
        foreach (var b in FindObjectsByType<CampBuilding>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (b == null) continue;
            if (b.name.ToLowerInvariant().Contains(frag)) return b.transform;
            if (!string.IsNullOrEmpty(b.buildingName) && b.buildingName.ToLowerInvariant().Contains(frag)) return b.transform;
        }
        return null;
    }

    // Best point to aim the trail / marker at for a given target. Building
    // prefab pivots are often off to one side of the model, so aiming at
    // transform.position sends the trail past the visual building. We use
    // the combined renderer-bounds centre (XZ) instead, snapped to the
    // NavMesh so the path actually terminates on walkable ground next to
    // the building. Cached per-target (bounds don't move in camp).
    private readonly Dictionary<Transform, Vector3> aimCache = new Dictionary<Transform, Vector3>();

    private Vector3 GetAimPoint(Transform target)
    {
        if (target == null) return Vector3.zero;
        if (aimCache.TryGetValue(target, out var cached)) return cached;

        Vector3 aim = target.position;

        // For a CampBuilding, aim ONLY at its realModel (the built
        // structure). Encapsulating ALL child renderers pulled the centre
        // toward the resource-pile visuals that sit off to one side, which
        // is why the trail veered right of the lodge.
        var building = target.GetComponent<CampBuilding>();
        Renderer[] renderers = null;
        if (building != null && building.realModel != null)
            renderers = building.realModel.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
            renderers = target.GetComponentsInChildren<Renderer>();

        if (renderers != null && renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            aim = new Vector3(b.center.x, target.position.y, b.center.z);
        }

        // Snap to the nearest walkable navmesh point so the A* path ends
        // ON ground next to the building, not inside its footprint.
        if (NavMesh.SamplePosition(aim, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            aim = hit.position;

        aimCache[target] = aim;
        return aim;
    }

    private void Update()
    {
        progressTimer += Time.deltaTime;
        if (progressTimer >= progressCheckInterval)
        {
            progressTimer = 0f;
            int newIdx = ComputeCurrentStepIndex();
            if (newIdx != currentStepIndex)
            {
                // Step-complete celebration — only when we ADVANCED past a
                // real step (not on first-compute or when steps regress).
                bool advanced = currentStepIndex >= 0
                             && currentStepIndex < steps.Count
                             && (newIdx > currentStepIndex || newIdx < 0);
                currentStepIndex = newIdx;
                RefreshUI();
                if (advanced)
                {
                    ToastManager.Show(LocalizationManager.Tr("GUIDE_STEP_DONE"), ToastManager.ToastKind.Achievement);
                    if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_QuestAccept);
                    StartCoroutine(PlateAdvanceRoutine());
                }
                else
                {
                    RefreshPlate();
                }
            }
        }

        // Off-screen arrow — same target as the beacon. Works fine in
        // parallel: this is a screen-edge UI arrow, the beacon is a
        // world-space marker over the objective.
        if (OffscreenWaypointIndicator.Instance != null)
        {
            if (currentStepIndex >= 0 && currentStepIndex < steps.Count)
            {
                var t = steps[currentStepIndex].target;
                if (t != null) OffscreenWaypointIndicator.Instance.SetTarget(t);
                else OffscreenWaypointIndicator.Instance.ClearTarget();
            }
            else OffscreenWaypointIndicator.Instance.ClearTarget();
        }

        // Follow the current target with the waypoint marker — with a slow
        // bob + spin so the beacon reads as a live objective, not debris.
        if (waypointMarker != null && currentStepIndex >= 0 && currentStepIndex < steps.Count)
        {
            var t = steps[currentStepIndex].target;
            if (t != null)
            {
                // Aim at the visual centre, not the pivot — many building
                // prefabs have their pivot off to one side of the model.
                Vector3 aim = GetAimPoint(t);
                float bob = Mathf.Sin(Time.time * 2f) * 0.35f;
                waypointMarker.transform.position = aim + Vector3.up * (markerYOffset + bob);
                waypointMarker.transform.Rotate(0f, 40f * Time.deltaTime, 0f, Space.World);
                if (!waypointMarker.activeSelf) waypointMarker.SetActive(true);
            }
            else if (waypointMarker.activeSelf)
            {
                // Prompt-only step (target undiscovered) — hide the beacon.
                waypointMarker.SetActive(false);
            }
        }
        else if (waypointMarker != null && waypointMarker.activeSelf)
        {
            waypointMarker.SetActive(false);
        }

        // Live distance readout appended to the prompt (updates at 2 Hz
        // via the same throttle as the trail).
        if (promptText != null && player != null
            && currentStepIndex >= 0 && currentStepIndex < steps.Count)
        {
            var step = steps[currentStepIndex];
            if (step.target != null && Time.frameCount % 30 == 0)
            {
                float dist = Vector3.Distance(player.position, GetAimPoint(step.target));
                promptText.text = $"{LocalizationManager.Tr(step.promptKey)}  <color=#B0A080>· {Mathf.RoundToInt(dist)}m</color>";
            }
        }

        // Trail line — path from player to current target via NavMesh.
        // Recomputed on a throttle; smoothed toward its new shape every
        // frame so the ribbon glides instead of snapping between the
        // 4-Hz recomputes.
        trailTimer += Time.deltaTime;
        if (trailTimer >= trailUpdateInterval && trailLine != null && player != null
            && currentStepIndex >= 0 && currentStepIndex < steps.Count)
        {
            trailTimer = 0f;
            var target = steps[currentStepIndex].target;
            if (target != null && NavMesh.CalculatePath(player.position, GetAimPoint(target), NavMesh.AllAreas, scratchPath)
                && scratchPath.corners.Length >= 2)
            {
                RebuildSmoothTrail(scratchPath.corners);
            }
            else
            {
                trailLine.enabled = false;
                smoothedTrail = null;
            }
        }

        // Per-frame glide of the actually-rendered points toward the
        // freshly-computed smooth target — removes the visible pop each
        // time the path recomputes.
        if (trailLine != null && trailLine.enabled && smoothedTrail != null && renderedTrail != null)
        {
            float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
            for (int i = 0; i < renderedTrail.Length; i++)
            {
                renderedTrail[i] = Vector3.Lerp(renderedTrail[i], smoothedTrail[i], k);
                trailLine.SetPosition(i, renderedTrail[i]);
            }
        }
    }

    // Catmull-Rom subdivision of the raw NavMesh corners into a dense,
    // rounded polyline. NavMesh corners are hard angles at every obstacle;
    // subdividing turns them into a flowing ribbon.
    private Vector3[] smoothedTrail;   // target shape (recomputed on throttle)
    private Vector3[] renderedTrail;   // eased shape actually drawn
    [Tooltip("Subdivisions between each pair of NavMesh corners. Higher = smoother, denser line.")]
    public int trailSubdivisions = 8;

    private void RebuildSmoothTrail(Vector3[] corners)
    {
        int n = corners.Length;
        // Padded control points so the spline reaches the true endpoints.
        Vector3 First() => corners[0] + (corners[0] - corners[1]);
        Vector3 Last() => corners[n - 1] + (corners[n - 1] - corners[n - 2]);

        int sub = Mathf.Max(1, trailSubdivisions);
        int outCount = (n - 1) * sub + 1;
        var pts = new Vector3[outCount];
        int w = 0;
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 p0 = i == 0 ? First() : corners[i - 1];
            Vector3 p1 = corners[i];
            Vector3 p2 = corners[i + 1];
            Vector3 p3 = (i + 2 < n) ? corners[i + 2] : Last();
            int segSteps = (i == n - 2) ? sub : sub; // uniform
            for (int s = 0; s < segSteps; s++)
            {
                float t = s / (float)sub;
                pts[w++] = CatmullRom(p0, p1, p2, p3, t) + Vector3.up * 0.08f;
            }
        }
        pts[w] = corners[n - 1] + Vector3.up * 0.08f;

        smoothedTrail = pts;
        // Re-seed the rendered buffer if the point count changed so the
        // per-frame lerp has a matching-length source.
        if (renderedTrail == null || renderedTrail.Length != pts.Length)
            renderedTrail = (Vector3[])pts.Clone();

        trailLine.positionCount = pts.Length;
        for (int i = 0; i < pts.Length; i++) trailLine.SetPosition(i, renderedTrail[i]);
        if (!trailLine.enabled) trailLine.enabled = true;
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private void RecomputeCurrentStep()
    {
        currentStepIndex = ComputeCurrentStepIndex();
    }

    // Returns index of the first uncompleted step, or -1 when all done.
    private int ComputeCurrentStepIndex()
    {
        for (int i = 0; i < steps.Count; i++)
        {
            if (!IsStepCompleted(steps[i])) return i;
        }
        return -1;
    }

    private bool IsStepCompleted(GuideStep step)
    {
        if (step == null || string.IsNullOrEmpty(step.playerPrefsKey)) return true;
        return PlayerPrefs.GetInt(step.playerPrefsKey, 0) >= step.requiredValue;
    }

    private void RefreshUI()
    {
        if (promptText != null)
        {
            if (currentStepIndex < 0 || currentStepIndex >= steps.Count)
            {
                promptText.text = "";
                promptText.gameObject.SetActive(false);
            }
            else
            {
                promptText.gameObject.SetActive(true);
                promptText.text = LocalizationManager.Tr(steps[currentStepIndex].promptKey);
            }
        }
        if (trailLine != null && (currentStepIndex < 0 || currentStepIndex >= steps.Count))
        {
            trailLine.enabled = false;
            trailLine.positionCount = 0;
        }
    }
}
