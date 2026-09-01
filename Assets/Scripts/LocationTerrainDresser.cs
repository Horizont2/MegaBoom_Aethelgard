using System.Collections.Generic;
using UnityEngine;

// Makes a LOCATION's own Terrain blend into the procedurally-generated world.
// Put this on the location's Terrain object (the one that ships inside the
// prefab). After the world is generated it:
//   * MatchLayers  — swaps the terrain's layers for the world's grass/sand/
//                    snow/rock layers and repaints them by height / steepness /
//                    distance-above-water, so the colour matches the world;
//   * AddGrass     — copies the world's grass detail prototypes and scatters
//                    grass on the gentle grassy areas;
//   * ScatterTrees — plants the world's trees on bare, gentle, above-water
//                    spots outside the built core;
//   * AddLandform  — adds gentle height variation to the flat outer areas so
//                    the ground doesn't read as a dead-flat plane.
//
// It reads all the shared assets (layers, trees, detail prototypes) from the
// scene's WorldGenerator, so nothing has to be re-wired here.
//
// Everything is toggleable and everything except MatchLayers keeps clear of a
// central "built core" radius so buildings/roads aren't touched.
[RequireComponent(typeof(Terrain))]
public class LocationTerrainDresser : MonoBehaviour
{
    [Header("What to do")]
    public bool matchLayers = true;
    public bool addGrass = true;
    public bool scatterTrees = true;
    [Tooltip("Gentle height variation on flat OUTER areas. Off by default: it edits the terrain heightmap, which can lift the ground into props/buildings if they sit on this terrain. Enable only if the outer ring is empty.")]
    public bool addLandform = false;

    [Header("Water")]
    [Tooltip("The location's water object — used to know where the shoreline is (sand band) and to keep trees/landform above water. If left empty, tries the SelfContainedLocation.waterReference on this object or a parent.")]
    public Transform waterReference;
    [Tooltip("Metres above the water line painted as sand/shore.")]
    public float shoreBand = 2.5f;

    [Header("Built core (kept clear of trees/grass/landform)")]
    [Tooltip("Local-space centre of the built village core (relative to the terrain). Trees/grass/landform stay outside keepClearRadius of it.")]
    public Vector3 coreCenterLocal = Vector3.zero;
    public float keepClearRadius = 60f;

    [Header("Tuning")]
    [Range(10f, 60f)] public float steepRockAngle = 32f;
    [Range(0f, 1f)] public float snowHeightFrac = 0.72f;
    public int treeCount = 120;
    public float landformAmplitude = 2.0f;
    public float landformScale = 0.02f;

    private void OnEnable()
    {
        WorldGenerator.OnWorldGenerationComplete += Run;
        if (WorldGenerator.IsGenerationDone) Invoke(nameof(Run), 0.1f);
    }
    private void OnDisable() => WorldGenerator.OnWorldGenerationComplete -= Run;

    private bool _done;

    [ContextMenu("Dress Terrain Now")]
    public void Run()
    {
        if (_done) return;
        _done = true;
        WorldGenerator.OnWorldGenerationComplete -= Run;

        Terrain locTerrain = GetComponent<Terrain>();
        if (locTerrain == null || locTerrain.terrainData == null) return;

        // Work on a runtime CLONE of the TerrainData so painting heights /
        // splat / details never writes back into the shared prefab asset
        // (that would permanently alter the location in the project).
        TerrainData clone = Instantiate(locTerrain.terrainData);
        locTerrain.terrainData = clone;
        var locCollider = locTerrain.GetComponent<TerrainCollider>();
        if (locCollider != null) locCollider.terrainData = clone;

        var wg = FindFirstObjectByType<WorldGenerator>();
        if (wg == null) { Debug.LogWarning("[LocationTerrainDresser] No WorldGenerator in scene — nothing to match to."); return; }

        if (waterReference == null)
        {
            var sc = GetComponentInParent<SelfContainedLocation>();
            if (sc != null) waterReference = sc.waterReference;
        }
        float waterY = waterReference != null ? waterReference.position.y : float.NegativeInfinity;

        try { if (matchLayers) DoMatchLayers(locTerrain, wg, waterY); }
        catch (System.Exception e) { Debug.LogError("[LocationTerrainDresser] MatchLayers failed: " + e); }

        try { if (addLandform) DoLandform(locTerrain, waterY); }
        catch (System.Exception e) { Debug.LogError("[LocationTerrainDresser] Landform failed: " + e); }

        try { if (addGrass) DoGrass(locTerrain, wg, waterY); }
        catch (System.Exception e) { Debug.LogError("[LocationTerrainDresser] Grass failed: " + e); }

        try { if (scatterTrees) DoTrees(locTerrain, wg, waterY); }
        catch (System.Exception e) { Debug.LogError("[LocationTerrainDresser] Trees failed: " + e); }

        locTerrain.Flush();
    }

    // ---- colour / splat ----
    private void DoMatchLayers(Terrain locTerrain, WorldGenerator wg, float waterY)
    {
        var layers = new List<TerrainLayer>();
        if (wg.grassLayer != null) layers.Add(wg.grassLayer);
        if (wg.sandLayer != null) layers.Add(wg.sandLayer);
        if (wg.snowLayer != null) layers.Add(wg.snowLayer);
        if (wg.rockLayer != null) layers.Add(wg.rockLayer);
        if (layers.Count == 0) return;

        TerrainData td = locTerrain.terrainData;
        td.terrainLayers = layers.ToArray();

        int iGrass = 0;
        int iSand = wg.sandLayer != null ? layers.IndexOf(wg.sandLayer) : -1;
        int iSnow = wg.snowLayer != null ? layers.IndexOf(wg.snowLayer) : -1;
        int iRock = wg.rockLayer != null ? layers.IndexOf(wg.rockLayer) : -1;

        int aw = td.alphamapWidth, ah = td.alphamapHeight, n = td.alphamapLayers;
        float[,,] maps = new float[ah, aw, n];
        Vector3 basePos = locTerrain.transform.position;

        for (int y = 0; y < ah; y++)
        {
            float v = ah > 1 ? (float)y / (ah - 1) : 0f;
            for (int x = 0; x < aw; x++)
            {
                float u = aw > 1 ? (float)x / (aw - 1) : 0f;
                float steep = td.GetSteepness(u, v);
                float worldY = basePos.y + td.GetInterpolatedHeight(u, v);

                int pick = iGrass;
                if (steep >= steepRockAngle && iRock >= 0) pick = iRock;
                else if (iSnow >= 0 && (td.GetInterpolatedHeight(u, v) / td.size.y) >= snowHeightFrac) pick = iSnow;
                else if (iSand >= 0 && waterY > float.NegativeInfinity && worldY <= waterY + shoreBand) pick = iSand;

                maps[y, x, pick] = 1f;
            }
        }
        td.SetAlphamaps(0, 0, maps);
    }

    // ---- gentle height variation on flat outer areas ----
    private void DoLandform(Terrain locTerrain, float waterY)
    {
        TerrainData td = locTerrain.terrainData;
        int res = td.heightmapResolution;
        float[,] h = td.GetHeights(0, 0, res, res);
        Vector3 basePos = locTerrain.transform.position;
        float seedX = Random.value * 1000f, seedZ = Random.value * 1000f;

        for (int y = 0; y < res; y++)
        {
            float v = res > 1 ? (float)y / (res - 1) : 0f;
            for (int x = 0; x < res; x++)
            {
                float u = res > 1 ? (float)x / (res - 1) : 0f;
                Vector3 local = new Vector3(u * td.size.x, 0f, v * td.size.z);
                if ((local - coreCenterLocal).sqrMagnitude < keepClearRadius * keepClearRadius) continue;
                float worldY = basePos.y + h[y, x] * td.size.y;
                if (worldY <= waterY + shoreBand) continue;               // don't raise the lakebed/shore
                if (td.GetSteepness(u, v) > 12f) continue;                 // only genuinely flat cells
                float n = Mathf.PerlinNoise(seedX + local.x * landformScale, seedZ + local.z * landformScale) - 0.5f;
                h[y, x] += (n * landformAmplitude) / td.size.y;
            }
        }
        td.SetHeights(0, 0, h);
    }

    // ---- grass details ----
    private void DoGrass(Terrain locTerrain, WorldGenerator wg, float waterY)
    {
        Terrain worldTerrain = wg.GetComponent<Terrain>();
        if (worldTerrain == null || worldTerrain.terrainData == null) return;
        var protos = worldTerrain.terrainData.detailPrototypes;
        if (protos == null || protos.Length == 0) return;

        TerrainData td = locTerrain.terrainData;
        if (td.detailWidth < 8) td.SetDetailResolution(512, 16);
        td.detailPrototypes = protos;
        Vector3 basePos = locTerrain.transform.position;

        int dw = td.detailWidth, dh = td.detailHeight;
        for (int p = 0; p < protos.Length; p++)
        {
            int[,] layer = new int[dh, dw];
            for (int y = 0; y < dh; y++)
            {
                float v = dh > 1 ? (float)y / (dh - 1) : 0f;
                for (int x = 0; x < dw; x++)
                {
                    float u = dw > 1 ? (float)x / (dw - 1) : 0f;
                    Vector3 local = new Vector3(u * td.size.x, 0f, v * td.size.z);
                    if ((local - coreCenterLocal).sqrMagnitude < keepClearRadius * keepClearRadius) continue;
                    if (td.GetSteepness(u, v) > 25f) continue;
                    float worldY = basePos.y + td.GetInterpolatedHeight(u, v);
                    if (worldY <= waterY + shoreBand) continue;
                    if (Random.value < 0.45f) layer[y, x] = Random.Range(1, 4);
                }
            }
            td.SetDetailLayer(0, 0, p, layer);
        }
    }

    // ---- scatter trees on bare gentle spots ----
    private void DoTrees(Terrain locTerrain, WorldGenerator wg, float waterY)
    {
        if (wg.baseTrees == null || wg.baseTrees.Length == 0) return;
        TerrainData td = locTerrain.terrainData;
        Vector3 basePos = locTerrain.transform.position;
        Transform container = new GameObject("LocationTrees").transform;
        container.SetParent(transform, false);

        int placed = 0, attempts = 0;
        while (placed < treeCount && attempts++ < treeCount * 20)
        {
            float u = Random.value, v = Random.value;
            Vector3 local = new Vector3(u * td.size.x, 0f, v * td.size.z);
            if ((local - coreCenterLocal).sqrMagnitude < keepClearRadius * keepClearRadius) continue;
            if (td.GetSteepness(u, v) > 24f) continue;
            float worldX = basePos.x + local.x;
            float worldZ = basePos.z + local.z;
            float worldY = basePos.y + td.GetInterpolatedHeight(u, v);
            if (worldY <= waterY + shoreBand + 0.5f) continue;            // keep out of the water/shore

            // Only plant where the ray actually lands on THIS terrain (not on a
            // building/prop), so trees don't sprout on rooftops or in the market.
            if (Physics.Raycast(new Vector3(worldX, worldY + 50f, worldZ), Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<Terrain>() != locTerrain) continue;
                worldY = hit.point.y;
            }

            GameObject prefab = wg.baseTrees[Random.Range(0, wg.baseTrees.Length)];
            if (prefab == null) continue;
            GameObject t = Instantiate(prefab, new Vector3(worldX, worldY, worldZ),
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), container);
            float s = Random.Range(0.85f, 1.25f);
            t.transform.localScale = Vector3.Scale(t.transform.localScale, new Vector3(s, s, s));
            placed++;
        }
    }
}
