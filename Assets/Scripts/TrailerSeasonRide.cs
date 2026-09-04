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

    [Header("Tree recolour (material SWAP per season — the game's foliage system)")]
    public bool tintTrees = true;
    [Tooltip("Season foliage materials. Left empty, they're read from the scene's WorldGenerator (which is NOT in the trailer scene — so the setup tool fills the table below instead).")]
    public Material birchAutumn, birchWinter, largeAutumn, largeWinter, bushAutumn, bushWinter;

    [Header("Foliage material table (filled by 'Setup Act II Seasons')")]
    [Tooltip("Every summer foliage material found in the scene, paired with its _Autumn / _Snow(_Winter) variant on disk. This is what actually recolours the trees, since they are scene GameObjects — not terrain trees.")]
    public Material[] foliageBase;
    public Material[] foliageAutumn;
    public Material[] foliageWinter;

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

    [Header("Snow look (the source prefab ships Unity's Default-Particle material)")]
    [Tooltip("Material used for the snow. The stock Snowfall prefab references Unity's built-in Default-Particle, which is a legacy shader and renders as a grey smear under URP. Assigned by 'Setup Act II Seasons'.")]
    public Material snowMaterial;
    [Tooltip("Flake size range in metres.")]
    public Vector2 snowSize = new Vector2(0.05f, 0.16f);
    [Tooltip("Flakes emitted per second.")]
    public float snowRate = 700f;
    [Tooltip("Fall speed range.")]
    public Vector2 snowFallSpeed = new Vector2(1.1f, 2.4f);
    [Tooltip("How much the flakes wander sideways as they fall.")]
    public float snowDrift = 0.55f;
    public Color snowTint = new Color(0.95f, 0.97f, 1f, 0.9f);

    private static readonly int SeasonColorID = Shader.PropertyToID("_SeasonColor");
    private static readonly int SeasonIndexID = Shader.PropertyToID("_SeasonIndex");
    private static readonly int BaseMapID = Shader.PropertyToID("_BaseMap");

    private GameObject _leaves, _snow;
    private int _terrainTexState = -1;   // 0 summer,1 autumn,2 winter
    private float _clock;
    private Renderer[] _trees;
    private int[] _treeSlot;           // which material slot to swap
    private Material[] _treeOrigMat;   // original (summer) material in that slot
    private Material[] _treeAutumnMat; // resolved autumn material for that slot
    private Material[] _treeWinterMat; // resolved winter material for that slot
    private int _treeSeason = -1;      // 0 summer,1 autumn,2 winter currently applied
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
        if (_snow != null) PolishSnow(_snow);
        if (_leaves) _leaves.SetActive(false);
        if (_snow) _snow.SetActive(false);
        if (tintTrees) GatherTrees();
        _treeSeason = -1;
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
        SwapTreeSeason(0);   // restore trees to summer materials
    }

    // The trees in this scene are ordinary GameObjects (NOT terrain trees — the
    // terrain reports 0 tree prototypes), so recolouring them means swapping the
    // MATERIAL in each renderer slot. Every summer material is paired with its
    // autumn / winter variant either from the table the setup tool filled, or by
    // the name heuristic below.
    private void GatherTrees()
    {
        // Pull the season materials from the scene's WorldGenerator if it exists
        // (it doesn't in the trailer scene — hence the foliage table).
        var wg = Object.FindFirstObjectByType<WorldGenerator>();
        if (wg != null)
        {
            if (birchAutumn == null) birchAutumn = wg.baseTreeAutumnMaterial;
            if (birchWinter == null) birchWinter = wg.baseTreeWinterMaterial;
            if (largeAutumn == null) largeAutumn = wg.giantTreeAutumnMaterial;
            if (largeWinter == null) largeWinter = wg.giantTreeWinterMaterial;
            if (bushAutumn == null) bushAutumn = wg.bushAutumnMaterial;
            if (bushWinter == null) bushWinter = wg.bushWinterMaterial;
        }

        var rends = new System.Collections.Generic.List<Renderer>();
        var slots = new System.Collections.Generic.List<int>();
        var orig = new System.Collections.Generic.List<Material>();
        var aut = new System.Collections.Generic.List<Material>();
        var win = new System.Collections.Generic.List<Material>();

        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            var mats = r.sharedMaterials;
            for (int s = 0; s < mats.Length; s++)
            {
                var m = mats[s]; if (m == null) continue;
                Material a = LookUp(m, foliageAutumn), w = LookUp(m, foliageWinter);
                if (a == null && w == null)
                {
                    // Fallback: the old birch/large/bush heuristic.
                    string n = m.name.ToLowerInvariant();
                    if (n.Contains("birch")) { a = birchAutumn; w = birchWinter; }
                    else if (n.Contains("treelarge") || n.Contains("giant") || n.Contains("large")) { a = largeAutumn; w = largeWinter; }
                    else if (n.Contains("bush")) { a = bushAutumn; w = bushWinter; }
                }
                if (a == null && w == null) continue;
                rends.Add(r); slots.Add(s); orig.Add(m); aut.Add(a); win.Add(w);
            }
        }
        _trees = rends.ToArray(); _treeSlot = slots.ToArray();
        _treeOrigMat = orig.ToArray(); _treeAutumnMat = aut.ToArray(); _treeWinterMat = win.ToArray();
        Debug.Log($"[Trailer] Foliage recolour: {_trees.Length} renderer slots matched (table entries: {(foliageBase != null ? foliageBase.Length : 0)}).");
    }

    private Material LookUp(Material m, Material[] table)
    {
        if (foliageBase == null || table == null) return null;
        for (int i = 0; i < foliageBase.Length && i < table.Length; i++)
            if (foliageBase[i] == m) return table[i];
        return null;
    }

    // Swap tree/bush leaves materials to the given season (0 summer,1 autumn,2 winter).
    private void SwapTreeSeason(int season)
    {
        if (_trees == null) return;
        for (int i = 0; i < _trees.Length; i++)
        {
            var r = _trees[i]; if (r == null) continue;
            Material target = _treeOrigMat[i];   // summer = original
            if (season == 1 && _treeAutumnMat[i] != null) target = _treeAutumnMat[i];
            else if (season == 2 && _treeWinterMat[i] != null) target = _treeWinterMat[i];
            var mats = r.sharedMaterials;
            if (_treeSlot[i] < mats.Length && mats[_treeSlot[i]] != target)
            {
                mats[_treeSlot[i]] = target;
                r.sharedMaterials = mats;
            }
        }
    }

    // When true, the sequence director drives the look via ApplyU() and this
    // component stops advancing on its own.
    [HideInInspector] public bool manual = false;

    // Public hook so the sequence director can drive the time-lapse / hold winter.
    public void ApplyU(float u) { Apply(Mathf.Clamp01(u)); }

    // LateUpdate so our sun/fog wins over DayNightCycle's own Update — no need to
    // disable the day/night system; we just override it during the trailer.
    private void LateUpdate()
    {
        if (manual) return;
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
            int season = u < 0.4f ? 0 : (u < 0.72f ? 1 : 2);
            if (season != _treeSeason) { SwapTreeSeason(season); _treeSeason = season; }
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

    // The stock Snowfall prefab is a grey smear: it uses Unity's built-in
    // Default-Particle material (a legacy shader URP does not light) with flakes
    // far too large and no drift. Re-dress the spawned INSTANCE — the shared
    // prefab asset is never touched.
    private void PolishSnow(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            if (snowMaterial != null) r.sharedMaterial = snowMaterial;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.alignment = ParticleSystemRenderSpace.View;
            r.minParticleSize = 0f;
            r.maxParticleSize = 0.06f;      // stops near flakes filling the screen
            r.sortMode = ParticleSystemSortMode.Distance;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // flakes stay put as the camera flies
            main.startSize = new ParticleSystem.MinMaxCurve(snowSize.x, snowSize.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(snowFallSpeed.x, snowFallSpeed.y);
            main.startColor = snowTint;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.02f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4.5f, 7f);

            var emission = ps.emission;
            emission.rateOverTime = snowRate;

            // Sideways wander so the fall isn't a straight vertical rain of dots.
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = snowDrift;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.35f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            // Fade in and out instead of popping into and out of existence.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f),
                        new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);

            ps.Clear(true);
            ps.Play(true);
        }
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
