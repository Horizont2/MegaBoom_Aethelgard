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

        [Tooltip("Optional number substituted into the prompt's {0}. Leave at -1 for prompts with no placeholder.")]
        // Exists so a step like 'hire 3 mercenaries' can take its number from
        // the same constant the objective is actually checked against, instead
        // of having it typed into eight translated strings where it would
        // silently stop matching the moment the squad size is retuned.
        public int promptArg = -1;

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

    [Header("Diagnostics")]
    [Tooltip("Log which scene object every guide step resolved to, once at start. Targets are discovered by type and by fuzzy name matching, so this is the fastest way to see WHY a trail is pointing somewhere unexpected. One block of text per camp load — safe to leave on.")]
    public bool logResolvedTargets = true;

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
    // Explicit agent-type filter so CalculatePath never has to guess. The
    // static NavMesh.CalculatePath(src, dst, areaMask, path) overload spammed
    // "could not determine precisely which agent type should move" once the
    // project had more than one baked agent type. Pinning the default
    // (Humanoid) agent type via a query filter silences it.
    private NavMeshQueryFilter guideFilter;

    private void Awake()
    {
        scratchPath = new NavMeshPath();
        guideFilter = new NavMeshQueryFilter
        {
            areaMask = NavMesh.AllAreas,
            agentTypeID = NavMesh.GetSettingsByIndex(0).agentTypeID
        };
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
            GuideStep step = steps[currentStepIndex];
            string body = step.promptArg >= 0
                ? LocalizationManager.Tr(step.promptKey, step.promptArg)
                : LocalizationManager.Tr(step.promptKey);
            missionPlate.Setup(LocalizationManager.Tr("GUIDE_PLATE_TITLE"), body, 0, 1);
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
        // Include INACTIVE — the barracks' built model is often disabled until
        // construction finishes, so the default (active-only) search returned
        // null and the hire/build-barracks steps had no trail. That left the
        // guide showing an earlier step (the storage vault, which sits next to
        // Elias), so the "hire a unit" trail appeared to lead to Elias.
        var barracks = FindFirstObjectByType<BarracksBuilding>(FindObjectsInactive.Include);
        CampBuilding barracksCB = barracks != null ? barracks.GetComponent<CampBuilding>() : null;
        // Fallback: resolve the barracks as a CampBuilding by id/name so the
        // step still points somewhere sensible even if the BarracksBuilding
        // component isn't found.
        if (barracksCB == null) barracksCB = FindBuilding("Barracks", "barrack");
        if (barracksCB != null)
        {
            barracksT = barracksCB.transform;
            // The companion CampBuilding's real buildingID drives the key so
            // the step credits even if the ID isn't literally "Barracks".
            if (!string.IsNullOrEmpty(barracksCB.buildingID))
                barracksKey = "SaveBld_" + barracksCB.buildingID;
        }
        else if (barracks != null)
        {
            barracksT = barracks.transform;
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
        // 3b/3c. THE UPGRADE LESSON, BEFORE THE FIRST FIGHT.
        //
        // This used to sit ten steps later, behind the whole mercenary arc, so
        // the player learned that gear can be improved only after they had
        // already fought a region without improving any. Teaching a system
        // after the moment it would first have helped is teaching it twice.
        //
        // Elias funds it, the trail points at the shop, and the shop's own
        // spotlight walkthrough takes over once they are inside — see
        // ShopTutorialDirector. Both steps sit on the critical path because a
        // player who skips this arrives at their first region with the weakest
        // helmet in the game and no idea that was a choice.
        steps.Add(new GuideStep { promptKey = "GUIDE_TALK_ELIAS_HELMET", target = eliasT, playerPrefsKey = "Elias_Helmet",           requiredValue = 1 });
        steps.Add(new GuideStep { promptKey = "GUIDE_UPGRADE_HELMET",    target = shopT,  playerPrefsKey = ShopTutorialDirector.PP_DONE, requiredValue = 1 });

        steps.Add(new GuideStep { promptKey = "GUIDE_CONQUER_FIRST",  target = mapT,      playerPrefsKey = "TotalConqueredRegions", requiredValue = 1 });

        // ---- The mercenary arc ------------------------------------------
        //
        // This block used to sit at the far end of the chain, behind the
        // storage vault and the notice board. That put the game's second
        // region — which is an auto-battle region and CANNOT be raided in
        // person — behind three errands the player had no reason to connect
        // to it. Whichever order they wandered in, the map offered them a
        // territory they had no army for and no prompt explaining why.
        //
        // So the arc now follows the first conquest immediately, and it reads
        // as one continuous thought: Elias funds you, you spend it, you send
        // the company, the province falls. The errands keep their order behind
        // it — they are useful, they are just not urgent.

        // 5. Elias has news, and a purse — see CampNPC_Elias's war-chest beat
        steps.Add(new GuideStep { promptKey = "GUIDE_TALK_ELIAS_AGAIN", target = eliasT,  playerPrefsKey = "Elias_WarChest",        requiredValue = 1 });
        // 6. Make sure the barracks stands. BarracksBuilding seeds itself to
        //    level 1 on first visit, so for most players this credits at once
        //    — it is here for the save where it has not.
        //
        //    Only added when a barracks was actually found. Its key is only
        //    ever written by the building itself, so on a camp scene without
        //    one the step could never complete — and now that it sits on the
        //    critical path, an uncompletable step would wedge the whole guide
        //    short of the objective this arc exists to deliver.
        if (barracksT != null)
            steps.Add(new GuideStep { promptKey = "GUIDE_BUILD_BARRACKS", target = barracksT, playerPrefsKey = barracksKey,         requiredValue = 1 });
        // 7. Raise the company Elias just paid for. Counts lifetime hires, and
        //    takes both the number it asks for and the number it checks from
        //    the one constant, so the objective and its text cannot disagree.
        steps.Add(new GuideStep { promptKey = "GUIDE_HIRE_SQUAD",     target = barracksT, playerPrefsKey = MercenaryRoster.PP_HIRED_TOTAL, requiredValue = MercenaryRoster.GuideSquadSize, promptArg = MercenaryRoster.GuideSquadSize });
        // 8. Send them at the second region
        steps.Add(new GuideStep { promptKey = "GUIDE_SEND_ARMY",      target = mapT,      playerPrefsKey = "MercFirstDeployed",     requiredValue = 1 });

        // ---- Camp errands, in their original order ----------------------
        // 9. Build the storage vault so more resource capacity unlocks.
        //    Key derived from the real building above, not hardcoded.
        steps.Add(new GuideStep { promptKey = "GUIDE_BUILD_STORAGE",  target = storageT,  playerPrefsKey = storageKey,  requiredValue = 1 });
        // 10. Check the notice board for daily missions (extra income)
        steps.Add(new GuideStep { promptKey = "GUIDE_NOTICE_BOARD",   target = noticeT,   playerPrefsKey = "HasInteractedWithBoard", requiredValue = 1 });
        // 11. Visit the Shop and spend a diamond on gear
        steps.Add(new GuideStep { promptKey = "GUIDE_VISIT_SHOP",     target = shopT,     playerPrefsKey = "ShopFirstPurchase",     requiredValue = 1 });
        // 12. Reach the mid-game location (R8 Sunken Outpost — 3rd hand-built)
        steps.Add(new GuideStep { promptKey = "GUIDE_MIDGAME_REGION", target = mapT,      playerPrefsKey = "TotalConqueredRegions", requiredValue = 8 });
        // 13. Reach the city (R22 — pre-final hand-built)
        steps.Add(new GuideStep { promptKey = "GUIDE_REACH_CITY",     target = mapT,      playerPrefsKey = "TotalConqueredRegions", requiredValue = 21 });
        // 14. Final push: the Throne (R24)
        steps.Add(new GuideStep { promptKey = "GUIDE_FINAL_PUSH",     target = mapT,      playerPrefsKey = "TotalConqueredRegions", requiredValue = 24 });

        if (logResolvedTargets) LogResolvedTargets();
        WarnOnDuplicateBuildingIDs();
    }

    // Every step, what it resolved to, and where that is.
    //
    // "The trail goes to the wrong place" is otherwise a guessing game: the
    // targets are discovered by component type and by fuzzy name matching
    // against whatever is in the scene, so a renamed object or a prefab reused
    // as decoration can silently hand a step the wrong transform. One line per
    // step turns that into a fact you can read.
    private void LogResolvedTargets()
    {
        var sb = new System.Text.StringBuilder("[CampGuide] Resolved step targets:\n");
        for (int i = 0; i < steps.Count; i++)
        {
            GuideStep s = steps[i];
            sb.Append($"  {i}. {s.promptKey}  key={s.playerPrefsKey}>={s.requiredValue}  ->  ");
            sb.AppendLine(s.target == null
                ? "(no target — prompt only)"
                : $"'{s.target.name}' at {s.target.position}");
        }
        Debug.Log(sb.ToString());
    }

    // Two CampBuildings sharing a buildingID share their save key, because
    // CampBuilding derives it as "SaveBld_" + buildingID. Building one then
    // marks the other built, and any guide step keyed on that ID credits for
    // the wrong structure. Cheap to check, invisible until it bites.
    private void WarnOnDuplicateBuildingIDs()
    {
        var seen = new Dictionary<string, CampBuilding>();
        foreach (var b in FindObjectsByType<CampBuilding>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (b == null || string.IsNullOrEmpty(b.buildingID)) continue;
            if (seen.TryGetValue(b.buildingID, out var first))
            {
                Debug.LogWarning($"[CampGuide] '{b.name}' and '{first.name}' both use buildingID " +
                                 $"'{b.buildingID}', so they share the save key SaveBld_{b.buildingID} — " +
                                 "building either one marks both built, and guide steps keyed on it will " +
                                 "credit for the wrong building. Give them distinct IDs.");
                continue;
            }
            seen[b.buildingID] = b;
        }
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
        // ==== THE PORTAL IS CALLED SOMETHING ELSE ====
        //
        // There is no ExtractionPortal anywhere in CampScene, and the exact-name
        // lookup below asked for "Shop", "ShopPortal" or "ShopInteract" — while
        // the object in the scene is named "Portal_To_Shop". So this returned
        // null (or, worse, the shop BUILDING named "Shop", which is not where
        // the player has to stand), the step got a null target, and the trail to
        // the shop simply never drew. The mission still advanced, so nothing
        // ever reported a fault.
        //
        // Matching by fragments instead of by exact string is what makes this
        // survive the next rename, and inactive objects are included because a
        // portal can easily be disabled until its step comes up.
        Transform best = null;
        int bestScore = 0;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null) continue;
            string n = t.name.ToLowerInvariant();
            if (!n.Contains("shop")) continue;

            // A thing you walk into beats a thing you look at.
            int score = 1;
            if (n.Contains("portal") || n.Contains("teleport") || n.Contains("door")) score = 3;
            else if (n.Contains("interact") || n.Contains("enter")) score = 2;
            // Spawn points and cameras named after the shop are not the shop.
            if (n.Contains("spawn") || n.Contains("vcam") || n.Contains("camera") || n.Contains("exit")) continue;

            if (score > bestScore) { bestScore = score; best = t; }
        }
        if (best != null) return best;

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
    // the building.
    //
    // Cached, but the cache remembers WHERE the target stood when it was
    // computed and drops itself when the target moves. "Bounds don't move in
    // camp" is true of buildings and false of the one target that is a person:
    // Elias patrols. His entry was computed the first time the guide looked at
    // him and then never again, so his beacon and his trail stayed pinned to
    // the spot by his hut where he happened to be standing at scene load,
    // while the man himself walked off. For a building the comparison is
    // against a value that never changes, so nothing is recomputed.
    private readonly Dictionary<Transform, (Vector3 from, Vector3 aim)> aimCache =
        new Dictionary<Transform, (Vector3, Vector3)>();

    // A path worth DRAWING — which is a stricter thing than a path that exists.
    //
    // NavMesh.CalculatePath returns true for a PARTIAL path: the destination
    // was unreachable, so it hands back the route to the closest polygon it
    // could get to and reports success. The old check took that at face value
    // and drew the ribbon anyway, so whenever an objective sat off the baked
    // NavMesh — behind a fence, on ground added after the last bake — the guide
    // confidently drew a gold trail to somewhere that was not the objective and
    // gave the player no hint it had given up. A trail that lies is worse than
    // no trail: the beacon over the target and the screen-edge arrow both read
    // straight off the world position and cannot be wrong, so when the route is
    // in doubt those are left to do the job alone.
    private bool TryPathTo(Vector3 aim)
    {
        if (!NavMesh.CalculatePath(player.position, aim, guideFilter, scratchPath)) return false;
        if (scratchPath.corners.Length < 2) return false;

        // Judge the path by WHERE IT ENDS UP, not by its status flag.
        //
        // The previous version demanded PathComplete and a landing within three
        // metres, and that turned out to hide almost every trail in the camp.
        // Both halves were wrong for this job. A route that ends on the walkable
        // ground beside a building is a perfectly good route, and a building's
        // aim point sits at the centre of its footprint — which is inside the
        // building, off the navmesh, so the path legitimately stops at its edge
        // and legitimately reports PathPartial.
        //
        // What actually needs catching is the failure this check exists for: a
        // path that gives up somewhere else entirely and draws a confident gold
        // ribbon to the wrong place. A generous landing radius catches that and
        // nothing else.
        Vector3 end = scratchPath.corners[scratchPath.corners.Length - 1];
        float dx = end.x - aim.x, dz = end.z - aim.z;
        if (dx * dx + dz * dz <= trailArrivalRadius * trailArrivalRadius) return true;

        // Said once per target rather than four times a second, so a genuinely
        // unreachable objective is visible in the log without burying it.
        if (_lastUnreachableAim != aim)
        {
            _lastUnreachableAim = aim;
            Debug.LogWarning($"[CampGuide] No route reaches {aim} — the path stops {Mathf.Sqrt(dx * dx + dz * dz):F1}m " +
                             $"short ({scratchPath.status}). Hiding the trail; the beacon and the arrow still point at it. " +
                             "Usually means the objective sits outside the baked NavMesh.");
        }
        return false;
    }

    [Tooltip("How close a route has to get to the objective to count as reaching it. Generous on purpose: a building's aim point is inside its own footprint, so a good path stops at the edge of it.")]
    public float trailArrivalRadius = 14f;
    private Vector3 _lastUnreachableAim = new Vector3(float.NaN, 0f, 0f);

    private Vector3 GetAimPoint(Transform target)
    {
        if (target == null) return Vector3.zero;
        if (aimCache.TryGetValue(target, out var cached)
            && (cached.from - target.position).sqrMagnitude < 0.25f)
            return cached.aim;

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

        aimCache[target] = (target.position, aim);
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
            if (target != null && TryPathTo(GetAimPoint(target)))
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
