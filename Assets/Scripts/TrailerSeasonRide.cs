using UnityEngine;
using UnityEngine.Playables;

// Drives a full SUMMER -> AUTUMN -> WINTER transformation across the trailer ride
// (Act II as a continuation of Act I's gallop). Uses the same mechanism the game
// already ships: a global shader tint the vegetation reads (_SeasonColor /
// _SeasonIndex), the terrain material's _BaseMap texture, and sun + fog colour —
// plus falling leaves / snow that blow past the camera.
//
// It resets everything it touched on disable, so it never leaves the shared
// terrain material or shader globals altered outside the trailer.
public class TrailerSeasonRide : MonoBehaviour
{
    [Header("Sync")]
    [Tooltip("Seconds over which Summer->Autumn->Winter plays (match the ride length). Uses its own clock from enable, so it spans the whole ride even past the Act I timeline.")]
    public float seasonDuration = 34f;
    [Tooltip("Optional — kept for inspector reference; the driver uses its own scaled clock.")]
    public PlayableDirector director;

    [Header("Terrain ground texture (rpgpp_lt_mat_a)")]
    public Material terrainMaterial;
    public Texture summerTexture;
    public Texture autumnTexture;
    public Texture winterTexture;

    [Header("Lighting")]
    public Light sun;
    public Color sunSummer = new Color(1f, 0.95f, 0.8f);
    public Color sunAutumn = new Color(1f, 0.7f, 0.4f);
    public Color sunWinter = new Color(0.7f, 0.8f, 1f);
    public Color fogSummer = new Color(0.5f, 0.58f, 0.5f);
    public Color fogAutumn = new Color(0.6f, 0.45f, 0.32f);
    public Color fogWinter = new Color(0.62f, 0.7f, 0.8f);

    [Header("Vegetation tint (global shader)")]
    public Color tintSummer = Color.white;
    public Color tintAutumn = new Color(0.85f, 0.5f, 0.2f);
    public Color tintWinter = new Color(0.9f, 0.92f, 1f);

    [Header("Falling VFX (follow the camera)")]
    public GameObject leavesPrefab;
    public GameObject snowPrefab;
    public Camera cam;

    private static readonly int SeasonColorID = Shader.PropertyToID("_SeasonColor");
    private static readonly int SeasonIndexID = Shader.PropertyToID("_SeasonIndex");
    private static readonly int BaseMapID = Shader.PropertyToID("_BaseMap");

    private GameObject _leaves, _snow;
    private int _terrainTexState = -1;   // 0 summer,1 autumn,2 winter
    private float _clock;

    private void OnEnable()
    {
        if (cam == null) cam = Camera.main;
        _clock = 0f;
        _terrainTexState = -1;
        _leaves = SpawnFollower(leavesPrefab, "Trailer_Leaves");
        _snow = SpawnFollower(snowPrefab, "Trailer_Snow");
        if (_leaves) _leaves.SetActive(false);
        if (_snow) _snow.SetActive(false);
        Apply(0f);
    }

    private void OnDisable()
    {
        // Restore the shared assets/globals so nothing bleeds outside the trailer.
        Shader.SetGlobalColor(SeasonColorID, Color.white);
        Shader.SetGlobalFloat(SeasonIndexID, 0f);
        if (terrainMaterial != null && summerTexture != null) terrainMaterial.SetTexture(BaseMapID, summerTexture);
        if (_leaves) _leaves.SetActive(false);
        if (_snow) _snow.SetActive(false);
    }

    private void Update()
    {
        // Own scaled clock — matches the horse ride (which also starts at t=0),
        // and keeps advancing after the Act I timeline ends so seasons cover the
        // whole journey.
        _clock += Time.deltaTime;
        Apply(seasonDuration > 0.01f ? Mathf.Clamp01(_clock / seasonDuration) : 0f);
    }

    // u = 0..1 across the ride. Summer -> Autumn -> Winter, holding each a beat.
    private void Apply(float u)
    {
        Color tint, sunC, fogC;
        if (u < 0.5f)
        {
            float k = Smooth(Mathf.InverseLerp(0.18f, 0.5f, u));   // hold summer, then blend
            tint = Color.Lerp(tintSummer, tintAutumn, k);
            sunC = Color.Lerp(sunSummer, sunAutumn, k);
            fogC = Color.Lerp(fogSummer, fogAutumn, k);
        }
        else
        {
            float k = Smooth(Mathf.InverseLerp(0.5f, 0.85f, u));   // autumn -> winter, hold winter
            tint = Color.Lerp(tintAutumn, tintWinter, k);
            sunC = Color.Lerp(sunAutumn, sunWinter, k);
            fogC = Color.Lerp(fogAutumn, fogWinter, k);
        }

        Shader.SetGlobalColor(SeasonColorID, tint);
        Shader.SetGlobalFloat(SeasonIndexID, u < 0.4f ? 0f : (u < 0.72f ? 1f : 2f));

        if (sun != null) sun.color = sunC;
        RenderSettings.fogColor = fogC;

        // Terrain ground texture switches at the season midpoints.
        int wantTex = u < 0.4f ? 0 : (u < 0.72f ? 1 : 2);
        if (wantTex != _terrainTexState && terrainMaterial != null)
        {
            var tex = wantTex == 0 ? summerTexture : (wantTex == 1 ? autumnTexture : winterTexture);
            if (tex != null) terrainMaterial.SetTexture(BaseMapID, tex);
            _terrainTexState = wantTex;
        }

        // Falling leaves through autumn; snow from late autumn into winter.
        SetActiveSafe(_leaves, u >= 0.3f && u < 0.75f);
        SetActiveSafe(_snow, u >= 0.62f);
    }

    private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    private static void SetActiveSafe(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }

    private GameObject SpawnFollower(GameObject prefab, string name)
    {
        if (prefab == null) return null;
        var go = Instantiate(prefab);
        go.name = name;
        var follow = go.GetComponent<TrailerRainFollow>();
        if (follow == null) follow = go.AddComponent<TrailerRainFollow>();
        follow.target = cam != null ? cam.transform : null;
        follow.splashOnlyOnTerrain = false;   // leaves/snow don't need collision restriction
        return go;
    }
}
