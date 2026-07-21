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
    private readonly NavMeshPath scratchPath = new NavMeshPath();

    private void Start()
    {
        var pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null) player = pObj.transform;

        if (waypointMarkerPrefab != null)
        {
            waypointMarker = Instantiate(waypointMarkerPrefab);
            waypointMarker.SetActive(false);
        }
        RecomputeCurrentStep();
        RefreshUI();
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
                currentStepIndex = newIdx;
                RefreshUI();
            }
        }

        // Follow the current target with the waypoint marker.
        if (waypointMarker != null && currentStepIndex >= 0 && currentStepIndex < steps.Count)
        {
            var t = steps[currentStepIndex].target;
            if (t != null)
            {
                waypointMarker.transform.position = t.position + Vector3.up * markerYOffset;
                if (!waypointMarker.activeSelf) waypointMarker.SetActive(true);
            }
        }
        else if (waypointMarker != null && waypointMarker.activeSelf)
        {
            waypointMarker.SetActive(false);
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
