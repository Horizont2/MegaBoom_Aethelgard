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

    [Header("Terrain")]
    public Terrain terrain;
    [Tooltip("Ground textures for the terrain's first layer (optional).")]
    public Texture2D summerGround, autumnGround, winterGround;

    [Header("Grass (detail) tint")]
    public Color grassAutumn = new Color(0.72f, 0.5f, 0.24f);
    public Color grassWinter = new Color(0.92f, 0.95f, 1.0f);
    [Range(0f, 1f)] public float autumnBlend = 0.7f;
    [Range(0f, 1f)] public float winterBlend = 0.9f;

    private TerrainData _origTD, _workTD;
    private TerrainCollider _collider;
    private Color[] _baseHealthy, _baseDry;
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
        for (int i = 0; i < det.Length; i++) { _baseHealthy[i] = det[i].healthyColor; _baseDry[i] = det[i].dryColor; }

        _texState = -1; _lastBlend = -1f; _clock = 0f;
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
        if (!_ready) return;
        float u = (driveByRideProgress && ride != null)
            ? Mathf.Clamp01(ride.progress01)
            : (seasonDuration > 0.01f ? Mathf.Clamp01((_clock += Time.deltaTime) / seasonDuration) : 0f);
        ApplyProgress(u);
    }

    private void ApplyProgress(float u)
    {
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
            _lastBlend = blend;
        }

        // Ground texture swap at season midpoints.
        int want = u < 0.4f ? 0 : (u < 0.72f ? 1 : 2);
        if (want != _texState)
        {
            var tex = want == 0 ? summerGround : (want == 1 ? autumnGround : winterGround);
            var layers = _workTD.terrainLayers;
            if (tex != null && layers != null && layers.Length > 0 && layers[0] != null)
            {
                // Clone the layer so we never edit the shared TerrainLayer asset.
                var clone = Instantiate(layers[0]);
                clone.diffuseTexture = tex;
                layers[0] = clone;
                _workTD.terrainLayers = layers;
            }
            _texState = want;
        }
    }

    private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}
