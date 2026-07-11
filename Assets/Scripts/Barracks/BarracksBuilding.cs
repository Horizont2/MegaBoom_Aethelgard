using System;
using UnityEngine;

// The camp's barracks. Sits on the same GameObject as (or beside) a regular
// CampBuilding for the F-panel + upgrade tooling — the CampBuilding drives
// the level-up UI and hold-E flow; this component just:
//   1. Force-completes level 1 on the first visit (auto-built at start).
//   2. Overrides the F-panel behaviour to open BarracksUpgradePanel instead
//      of the generic build/upgrade sheet.
//   3. Spawns wandering unit visuals near the barracks when idle mercs live
//      in the roster.
// Runs before CampBuilding so we can promote a first-time-play "level 0"
// to level 1 in PlayerPrefs BEFORE CampBuilding.Start() reads that key.
// Without the earlier execution the barracks flashes as "unbuilt" for the
// first frame — its realModel gets deactivated by SetupVisualsForCurrentLevel
// and the coroutine that used to run next-frame was too late.
[DefaultExecutionOrder(-100)]
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

        // Force the save flag to level 1 BEFORE CampBuilding.Start() runs.
        // CampBuilding.Start does `currentLevel = PP.GetInt("SaveBld_<id>",0)`
        // so if we don't seed the pref here the first frame reads 0 and
        // SetupVisualsForCurrentLevel hides realModel — the barracks
        // pops out of existence until the next-frame promotion catches up.
        if (autoBuildFirstLevel && host != null && !string.IsNullOrEmpty(host.buildingID))
        {
            string key = "SaveBld_" + host.buildingID;
            if (PlayerPrefs.GetInt(key, 0) < 1)
            {
                PlayerPrefs.SetInt(key, 1);
                PlayerPrefs.Save();
            }
        }
    }

    private void Start()
    {
        // Safety net: if for some reason the pref-seed above missed (e.g. no
        // buildingID set), still promote the runtime field so the barracks
        // never sits at level 0.
        if (autoBuildFirstLevel && host != null && host.currentLevel < 1)
        {
            host.currentLevel = 1;
            if (!string.IsNullOrEmpty(host.buildingID))
            {
                PlayerPrefs.SetInt("SaveBld_" + host.buildingID, 1);
                PlayerPrefs.Save();
            }
            if (host.ghostModel != null) host.ghostModel.SetActive(false);
            if (host.realModel != null) host.realModel.SetActive(true);
        }

        MercenaryRoster.OnRosterChanged += RefreshVisualUnits;
        RefreshVisualUnits();
    }

    private void OnDestroy()
    {
        MercenaryRoster.OnRosterChanged -= RefreshVisualUnits;
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
