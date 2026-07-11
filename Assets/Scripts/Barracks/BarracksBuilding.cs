using System;
using System.Collections;
using UnityEngine;

// The camp's barracks. Sits on the same GameObject as (or beside) a regular
// CampBuilding for the F-panel + upgrade tooling — the CampBuilding drives
// the level-up UI and hold-E flow; this component just:
//   1. Force-completes level 1 on the first visit (auto-built at start).
//   2. Overrides the F-panel behaviour to open BarracksUpgradePanel instead
//      of the generic build/upgrade sheet.
//   3. Spawns wandering unit visuals near the barracks when idle mercs live
//      in the roster.
[RequireComponent(typeof(CampBuilding))]
public class BarracksBuilding : MonoBehaviour, ICustomBuildingPanel
{
    [Header("Auto-Build")]
    [Tooltip("Якщо true, при першому старті сцени казарма буде примусово побудована до 1 лвла. Ставить у true.")]
    public bool autoBuildFirstLevel = true;

    [Header("Barracks-Specific UI")]
    // Wire the BarracksUpgradePanel prefab reference here — it replaces the
    // generic CampBuilding panel when the player presses F.
    public BarracksUpgradePanel barracksPanel;

    [Header("Unit Presence in Camp")]
    // Points near the barracks where unit visuals can wander. If empty,
    // units spawn at the barracks position.
    public Transform[] wanderPoints;
    public Transform unitSpawnPoint;
    // Maximum visual units to spawn — cap regardless of roster size so we
    // don't overrun the camp with 20+ NPCs.
    public int maxVisualUnits = 8;

    private CampBuilding host;
    private readonly System.Collections.Generic.List<BarracksUnitAI> spawned = new System.Collections.Generic.List<BarracksUnitAI>();

    private void Awake()
    {
        host = GetComponent<CampBuilding>();
    }

    private void Start()
    {
        if (autoBuildFirstLevel)
        {
            // If the CampBuilding decided level 0 (unbuilt), instantly promote to 1
            // so the barracks is available from the first frame.  We do this
            // in a late frame so CampBuilding.Start finishes its own setup.
            StartCoroutine(ForceLevel1NextFrame());
        }

        MercenaryRoster.OnRosterChanged += RefreshVisualUnits;
        RefreshVisualUnits();
    }

    private void OnDestroy()
    {
        MercenaryRoster.OnRosterChanged -= RefreshVisualUnits;
    }

    private IEnumerator ForceLevel1NextFrame()
    {
        yield return null;
        if (host != null && host.currentLevel < 1)
        {
            host.currentLevel = 1;
            PlayerPrefs.SetInt("SaveBld_" + host.buildingID, 1);
            PlayerPrefs.Save();

            // Nudge the visuals — swap ghost → real without playing the full
            // dust/hammer sequence. We just want it visible at level 1.
            if (host.ghostModel != null) host.ghostModel.SetActive(false);
            if (host.realModel != null) host.realModel.SetActive(true);
        }
    }

    private void RefreshVisualUnits()
    {
        if (MercenaryRoster.Instance == null) return;

        // Despawn extras.
        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            if (spawned[i] == null) { spawned.RemoveAt(i); continue; }
        }

        int aliveTotal = MercenaryRoster.Instance.CountAliveTotal();
        int shouldHave = Mathf.Min(aliveTotal, maxVisualUnits);

        // Drop extras first (roster shrunk).
        while (spawned.Count > shouldHave)
        {
            var last = spawned[spawned.Count - 1];
            spawned.RemoveAt(spawned.Count - 1);
            if (last != null) Destroy(last.gameObject);
        }

        // Now walk the roster and spawn missing visuals matching each alive unit.
        var all = MercenaryRoster.Instance.GetAllUnits();
        int visualIdx = 0;
        for (int i = 0; i < all.Count && visualIdx < shouldHave; i++)
        {
            var u = all[i];
            if (!u.alive) continue;
            if (visualIdx < spawned.Count)
            {
                visualIdx++;
                continue;
            }

            var data = MercenaryRoster.Instance.GetData(u.unitID);
            if (data == null || data.campPrefab == null) continue;

            Vector3 pos = unitSpawnPoint != null ? unitSpawnPoint.position : transform.position;
            var go = Instantiate(data.campPrefab, pos, Quaternion.identity);
            var ai = go.GetComponent<BarracksUnitAI>();
            if (ai == null) ai = go.AddComponent<BarracksUnitAI>();
            ai.wanderPoints = wanderPoints;
            ai.home = transform;
            spawned.Add(ai);
            visualIdx++;
        }
    }

    // Invoked by CampBuilding.OpenPanel when the player hits [F] on this
    // building — see ICustomBuildingPanel.
    public void OpenCustomPanel()
    {
        if (barracksPanel != null) barracksPanel.Open();
    }
}
