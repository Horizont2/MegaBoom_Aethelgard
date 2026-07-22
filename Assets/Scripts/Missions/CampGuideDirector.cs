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
    // Floating world-space marker prefab dropped over the current target
    // (usually a beam-of-light or arrow). Instantiated once, moved between
    // targets as steps advance.
    public GameObject waypointMarkerPrefab;
    public float markerYOffset = 3f;

    [Header("Trail Line (optional)")]
    // Attach a LineRenderer here — the guide will animate it from player
    // to the current target across the NavMesh. Leave null → no line.
    public LineRenderer trailLine;
    [Tooltip("How often (seconds) to re-path the trail. Higher = cheaper.")]
    public float trailUpdateInterval = 0.5f;
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

        if (waypointMarkerPrefab != null)
        {
            waypointMarker = Instantiate(waypointMarkerPrefab);
            waypointMarker.SetActive(false);
        }
        RecomputeCurrentStep();
        RefreshUI();
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
        var barracks = FindFirstObjectByType<BarracksBuilding>();
        if (barracks != null) barracksT = barracks.transform;

        steps.Add(new GuideStep { promptKey = "GUIDE_TALK_ELIAS",     target = eliasT,    playerPrefsKey = "Elias_Intro",            requiredValue = 1 });
        steps.Add(new GuideStep { promptKey = "GUIDE_BUILD_LODGE",    target = eliasT,    playerPrefsKey = "SaveBld_ScoutsLodge",    requiredValue = 2 });
        steps.Add(new GuideStep { promptKey = "GUIDE_USE_MAP_TABLE",  target = mapT,      playerPrefsKey = "Elias_TableBuilt",       requiredValue = 1 });
        steps.Add(new GuideStep { promptKey = "GUIDE_CONQUER_FIRST",  target = mapT,      playerPrefsKey = "TotalConqueredRegions",  requiredValue = 1 });
        steps.Add(new GuideStep { promptKey = "GUIDE_BUILD_BARRACKS", target = barracksT, playerPrefsKey = "SaveBld_Barracks",       requiredValue = 1 });
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
                }
            }
        }

        // Follow the current target with the waypoint marker — with a slow
        // bob + spin so the beacon reads as a live objective, not debris.
        if (waypointMarker != null && currentStepIndex >= 0 && currentStepIndex < steps.Count)
        {
            var t = steps[currentStepIndex].target;
            if (t != null)
            {
                float bob = Mathf.Sin(Time.time * 2f) * 0.35f;
                waypointMarker.transform.position = t.position + Vector3.up * (markerYOffset + bob);
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
                float dist = Vector3.Distance(player.position, step.target.position);
                promptText.text = $"{LocalizationManager.Tr(step.promptKey)}  <color=#B0A080>· {Mathf.RoundToInt(dist)}m</color>";
            }
        }

        // Trail line — path from player to current target via NavMesh.
        trailTimer += Time.deltaTime;
        if (trailTimer >= trailUpdateInterval && trailLine != null && player != null
            && currentStepIndex >= 0 && currentStepIndex < steps.Count)
        {
            trailTimer = 0f;
            var target = steps[currentStepIndex].target;
            if (target != null)
            {
                if (NavMesh.CalculatePath(player.position, target.position, NavMesh.AllAreas, scratchPath))
                {
                    int count = Mathf.Min(scratchPath.corners.Length, trailMaxCorners);
                    trailLine.positionCount = count;
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 c = scratchPath.corners[i];
                        c.y += 0.05f; // lift a touch above ground so the line doesn't z-fight
                        trailLine.SetPosition(i, c);
                    }
                    if (!trailLine.enabled) trailLine.enabled = true;
                }
                else
                {
                    trailLine.enabled = false;
                }
            }
        }
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
