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
    [Tooltip("Tie the seasons to the horse's PROGRESS along the route (recommended): summer at the start of the spline, winter at the end — so the route you draw is exactly the journey.")]
    public bool driveByRideProgress = true;
    [Tooltip("The horse ride to read progress from (auto-filled by the setup tool).")]
    public TrailerHorseRide ride;
    [Tooltip("Progress along the ride at which the season + day/night time-lapse BEGINS. Before this, Part 1 stays normal (summer, day). Set ~0.6 so it kicks in as the end-crane rises.")]
    [Range(0f, 1f)] public float startProgress = 0.6f;
    [Tooltip("Fallback only, when not driving by progress: seconds for Summer->Autumn->Winter.")]
    public float seasonDuration = 34f;
    [Tooltip("Optional — kept for inspector reference.")]
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

    [Header("Tree recolour (direct material tint on tree renderers)")]
    public bool tintTrees = true;
    [Tooltip("Objects whose name (or a parent's) contains any of these are treated as trees.")]
    public string[] treeNameHints = { "tree", "pine", "birch", "oak", "trunk", "foliage", "bush" };

    [Header("Day / Night (sun races on its orbit as he rides)")]
    public bool driveDayNight = true;
    [Tooltip("How many full day->night cycles across the whole ride.")]
    public float dayNightCycles = 2f;
    [Tooltip("Sun brightness at midday and at night.")]
    public float dayIntensity = 1.15f;
    public float nightIntensity = 0.05f;
    [Tooltip("Phase offset (degrees) so the reveal ends on a dramatic low sun/dawn.")]
    public float dayNightStartDeg = 20f;

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
    private Renderer[] _trees;
    private MaterialPropertyBlock _mpb;
    private int _treeTintStep = -99;
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private Quaternion _sunRot0;
    private float _sunIntensity0, _sunYaw;
    private bool _sunCached;

    private void OnEnable()
    {
        if (cam == null) cam = Camera.main;
        _clock = 0f;
        _terrainTexState = -1;
        if (sun != null && !_sunCached)
        {
            _sunRot0 = sun.transform.rotation;
            _sunIntensity0 = sun.intensity;
            _sunYaw = sun.transform.eulerAngles.y;   // orbit around the sun's own compass heading
            _sunCached = true;
        }
        _leaves = SpawnFollower(leavesPrefab, "Trailer_Leaves");
        _snow = SpawnFollower(snowPrefab, "Trailer_Snow");
        if (_leaves) _leaves.SetActive(false);
        if (_snow) _snow.SetActive(false);
        if (tintTrees) GatherTrees();
        _mpb = new MaterialPropertyBlock();
        _treeTintStep = -99;
        Apply(0f);
    }

    private void OnDisable()
    {
        // Restore the shared assets/globals so nothing bleeds outside the trailer.
        Shader.SetGlobalColor(SeasonColorID, Color.white);
        Shader.SetGlobalFloat(SeasonIndexID, 0f);
        if (terrainMaterial != null && summerTexture != null) terrainMaterial.SetTexture(BaseMapID, summerTexture);
        if (sun != null && _sunCached) { sun.transform.rotation = _sunRot0; sun.intensity = _sunIntensity0; }
        if (_leaves) _leaves.SetActive(false);
        if (_snow) _snow.SetActive(false);
        TintTrees(Color.white);   // restore trees
    }

    private void GatherTrees()
    {
        var all = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        var list = new System.Collections.Generic.List<Renderer>();
        foreach (var r in all)
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            string path = (r.transform.name + " " + (r.transform.parent ? r.transform.parent.name : "") +
                           " " + (r.transform.parent && r.transform.parent.parent ? r.transform.parent.parent.name : "")).ToLowerInvariant();
            foreach (var h in treeNameHints) if (path.Contains(h)) { list.Add(r); break; }
        }
        _trees = list.ToArray();
    }

    // Tint every tree renderer's base colour (MaterialPropertyBlock, so materials
    // aren't permanently modified).
    private void TintTrees(Color c)
    {
        if (_trees == null || _mpb == null) return;
        foreach (var r in _trees)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorID, c);
            _mpb.SetColor(ColorID, c);
            r.SetPropertyBlock(_mpb);
        }
    }

    // LateUpdate so our sun/fog wins over DayNightCycle's own Update — no need to
    // disable the day/night system; we just override it during the trailer.
    private void LateUpdate()
    {
        float raw;
        if (driveByRideProgress && ride != null)
        {
            // Seasons follow the route: 0 at the first knot, 1 at the last.
            raw = Mathf.Clamp01(ride.progress01);
        }
        else
        {
            _clock += Time.deltaTime;
            raw = seasonDuration > 0.01f ? Mathf.Clamp01(_clock / seasonDuration) : 0f;
        }
        // Hold summer/day until startProgress, then race through the time-lapse to
        // winter — so Part 1 is stable and this plays out at the end-crane.
        Apply(Mathf.InverseLerp(startProgress, 1f, raw));
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

        // Recolour the trees directly (the global tint above is a no-op unless a
        // shader reads it; this actually changes their look).
        if (tintTrees && _trees != null)
        {
            int step = Mathf.RoundToInt(u * 12f);
            if (step != _treeTintStep) { TintTrees(tint); _treeTintStep = step; }
        }

        float dayFactor = 1f;
        if (driveDayNight && sun != null && _sunCached)
        {
            // Sun races on its orbit across the ride.
            float pitch = dayNightStartDeg + u * dayNightCycles * 360f;
            sun.transform.rotation = Quaternion.Euler(pitch, _sunYaw, 0f);
            // Day when the sun is above the horizon, night when below.
            dayFactor = Mathf.Clamp01(Mathf.Sin(pitch * Mathf.Deg2Rad));
            sun.intensity = Mathf.Lerp(nightIntensity, dayIntensity, dayFactor);
        }

        if (sun != null) sun.color = sunC;
        // Darken fog + ambient at night so the day/night reads.
        RenderSettings.fogColor = fogC * Mathf.Lerp(0.35f, 1f, dayFactor);

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
