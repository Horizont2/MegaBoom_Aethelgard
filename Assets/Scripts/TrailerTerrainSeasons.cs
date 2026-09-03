using UnityEngine;

// Recolours the Unity Terrain + its painted detail GRASS as the ride progresses
// (summer green -> autumn browns -> winter white), plus swaps the terrain's
// ground texture per season.
//
// SAFETY: it works on a runtime CLONE of the TerrainData (never the real asset),
// and restores the original on disable — so it can NEVER corrupt your terrain.
public class TrailerTerrainSeasons : MonoBehaviour
{
    [Header("Sync")]
    public bool driveByRideProgress = true;
    public TrailerHorseRide ride;
    public float seasonDuration = 34f;
    [Tooltip("Progress along the ride at which the season change BEGINS (before this it stays summer). Set to ~0.6 so the change happens at the end-crane time-lapse.")]
    [Range(0f, 1f)] public float startProgress = 0.6f;

    [Header("Terrain")]
    public Terrain terrain;
    [Tooltip("OFF by default: the terrain's own layers aren't these textures, so swapping them repaints the ground with the wrong (default) texture. Leave off unless you assign the terrain's ACTUAL season splat textures.")]
    public bool swapGroundTexture = false;
    [Tooltip("Ground textures for the terrain's first layer (only used if swapGroundTexture is on).")]
    public Texture2D summerGround, autumnGround, winterGround;

    [Header("Grass (detail) tint")]
    public Color grassAutumn = new Color(0.72f, 0.5f, 0.24f);
    public Color grassWinter = new Color(0.92f, 0.95f, 1.0f);
    [Range(0f, 1f)] public float autumnBlend = 0.7f;
    [Range(0f, 1f)] public float winterBlend = 0.9f;

    [Header("TREES (terrain tree prototypes) — season prefab per prototype index")]
    [Tooltip("Assigned by 'Setup Act II Seasons' by matching each terrain tree prototype to its _Autumn / _Winter variant in Assets/GeneratedBiomeTrees.")]
    public GameObject[] autumnTreePrefabs;
    public GameObject[] winterTreePrefabs;

    [Header("GRASS (painted detail prototypes) — season prefab per detail index")]
    [Tooltip("For MESH-based painted grass/bushes the colour tint is ignored, so we swap the detail prototype's prefab instead. Assigned by 'Setup Act II Seasons'.")]
    public GameObject[] autumnDetailPrefabs;
    public GameObject[] winterDetailPrefabs;

    // When true the sequence director drives the look via ApplyU().
    [HideInInspector] public bool manual = false;
    public void ApplyU(float u) { if (_ready) ApplyProgress(Mathf.Clamp01(u)); }

    private TerrainData _origTD, _workTD;
    private TerrainCollider _collider;
    private Color[] _baseHealthy, _baseDry;
    private GameObject[] _origTreePrefabs;
    private GameObject[] _origDetailPrefabs;
    private int _treeState = -1;
    private int _detailState = -1;
    private int _texState = -1;
    private float _lastBlend = -1f;
    private float _clock;
    private bool _ready;

    private void OnEnable()
    {
        if (terrain == null) terrain = Terrain.activeTerrain;
        if (terrain == null || terrain.terrainData == null) return;

        // Swap in a CLONE so the source asset is never touched.
        _origTD = terrain.terrainData;
        _workTD = Instantiate(_origTD);
        _workTD.name = _origTD.name + " (TrailerClone)";
        terrain.terrainData = _workTD;
        _collider = terrain.GetComponent<TerrainCollider>();
        if (_collider != null) _collider.terrainData = _workTD;

        var det = _workTD.detailPrototypes;
        _baseHealthy = new Color[det.Length];
        _baseDry = new Color[det.Length];
        _origDetailPrefabs = new GameObject[det.Length];
        for (int i = 0; i < det.Length; i++)
        {
            _baseHealthy[i] = det[i].healthyColor;
            _baseDry[i] = det[i].dryColor;
            _origDetailPrefabs[i] = det[i].prototype;
        }

        // Cache the terrain's original tree prototype prefabs so we can swap them
        // to the season variants (this is how Terrain trees recolour — they're
        // TreeInstances, not renderers).
        var tp = _workTD.treePrototypes;
        _origTreePrefabs = new GameObject[tp.Length];
        for (int i = 0; i < tp.Length; i++) _origTreePrefabs[i] = tp[i].prefab;

        _texState = -1; _treeState = -1; _lastBlend = -1f; _clock = 0f;
        _ready = true;
        ApplyProgress(0f);
    }

    private void OnDisable() { RestoreTerrain(); }
    private void OnDestroy() { RestoreTerrain(); }

    private void RestoreTerrain()
    {
        if (terrain != null && _origTD != null) terrain.terrainData = _origTD;
        if (_collider != null && _origTD != null) _collider.terrainData = _origTD;
        if (_workTD != null) { Destroy(_workTD); _workTD = null; }
        _ready = false;
    }

    private void Update()
    {
        if (!_ready || manual) return;
        float raw = (driveByRideProgress && ride != null)
            ? Mathf.Clamp01(ride.progress01)
            : (seasonDuration > 0.01f ? Mathf.Clamp01((_clock += Time.deltaTime) / seasonDuration) : 0f);
        // Remap so the change only happens from startProgress -> 1 (summer until then).
        float u = Mathf.InverseLerp(startProgress, 1f, raw);
        ApplyProgress(u);
    }

    private void ApplyProgress(float u)
    {
        // TREES + painted GRASS: swap prototypes to the season variants.
        int season = u < 0.4f ? 0 : (u < 0.72f ? 1 : 2);
        if (season != _treeState) { SwapTreePrototypes(season); _treeState = season; }
        if (season != _detailState) { SwapDetailPrototypes(season); _detailState = season; }

        // Grass tint: summer (none) -> autumn -> winter.
        Color tint; float blend;
        if (u < 0.5f)
        {
            float k = Smooth(Mathf.InverseLerp(0.18f, 0.5f, u));
            tint = grassAutumn; blend = k * autumnBlend;
        }
        else
        {
            float k = Smooth(Mathf.InverseLerp(0.5f, 0.85f, u));
            tint = Color.Lerp(grassAutumn, grassWinter, k);
            blend = Mathf.Lerp(autumnBlend, winterBlend, k);
        }

        // Only push to the terrain when it changed enough (detail refresh is heavy).
        if (Mathf.Abs(blend - _lastBlend) > 0.03f && _baseHealthy != null)
        {
            var det = _workTD.detailPrototypes;
            for (int i = 0; i < det.Length && i < _baseHealthy.Length; i++)
            {
                det[i].healthyColor = Color.Lerp(_baseHealthy[i], tint, blend);
                det[i].dryColor = Color.Lerp(_baseDry[i], tint, blend);
            }
            _workTD.detailPrototypes = det;
            if (terrain != null) terrain.Flush();   // force the detail layer to re-render with new colours
            _lastBlend = blend;
        }

        // Ground texture swap — OFF by default (the terrain's real layers aren't
        // these textures, so swapping repaints the ground wrong).
        if (swapGroundTexture)
        {
            int want = u < 0.4f ? 0 : (u < 0.72f ? 1 : 2);
            if (want != _texState)
            {
                var tex = want == 0 ? summerGround : (want == 1 ? autumnGround : winterGround);
                var layers = _workTD.terrainLayers;
                if (tex != null && layers != null && layers.Length > 0 && layers[0] != null)
                {
                    var clone = Instantiate(layers[0]);
                    clone.diffuseTexture = tex;
                    layers[0] = clone;
                    _workTD.terrainLayers = layers;
                }
                _texState = want;
            }
        }
    }

    // Terrain trees are TreeInstances drawn from the terrain's tree PROTOTYPES —
    // recolouring them means pointing each prototype at its season-variant prefab
    // (done on the CLONE, so the real terrain asset is untouched).
    private void SwapTreePrototypes(int season)
    {
        if (_workTD == null || _origTreePrefabs == null) return;
        var tp = _workTD.treePrototypes;
        bool changed = false;
        for (int i = 0; i < tp.Length && i < _origTreePrefabs.Length; i++)
        {
            GameObject want = _origTreePrefabs[i];
            if (season == 1 && autumnTreePrefabs != null && i < autumnTreePrefabs.Length && autumnTreePrefabs[i] != null)
                want = autumnTreePrefabs[i];
            else if (season == 2 && winterTreePrefabs != null && i < winterTreePrefabs.Length && winterTreePrefabs[i] != null)
                want = winterTreePrefabs[i];
            if (tp[i].prefab != want) { tp[i].prefab = want; changed = true; }
        }
        if (!changed) return;
        _workTD.treePrototypes = tp;
        _workTD.RefreshPrototypes();
        if (terrain != null) terrain.Flush();
    }

    // Painted grass/bushes: for MESH detail prototypes the healthy/dry colours are
    // ignored, so point the prototype at its season-variant prefab instead.
    private void SwapDetailPrototypes(int season)
    {
        if (_workTD == null || _origDetailPrefabs == null) return;
        var det = _workTD.detailPrototypes;
        bool changed = false;
        for (int i = 0; i < det.Length && i < _origDetailPrefabs.Length; i++)
        {
            if (_origDetailPrefabs[i] == null) continue;      // texture-based grass → colour tint handles it
            GameObject want = _origDetailPrefabs[i];
            if (season == 1 && autumnDetailPrefabs != null && i < autumnDetailPrefabs.Length && autumnDetailPrefabs[i] != null)
                want = autumnDetailPrefabs[i];
            else if (season == 2 && winterDetailPrefabs != null && i < winterDetailPrefabs.Length && winterDetailPrefabs[i] != null)
                want = winterDetailPrefabs[i];
            if (det[i].prototype != want) { det[i].prototype = want; changed = true; }
        }
        if (!changed) return;
        _workTD.detailPrototypes = det;
        _workTD.RefreshPrototypes();
        if (terrain != null) terrain.Flush();
    }

    private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}
