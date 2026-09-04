using System.Collections.Generic;
using UnityEngine;

// Recolours the Unity Terrains + their painted detail GRASS as the trailer runs
// (summer green -> autumn browns -> winter white).
//
// Works on EVERY terrain in the scene (Part 1 and Part 2 have one each), and on a
// runtime CLONE of each TerrainData — the real assets are never touched and are
// restored on disable.
public class TrailerTerrainSeasons : MonoBehaviour
{
    [Header("Sync")]
    public bool driveByRideProgress = true;
    public TrailerHorseRide ride;
    public float seasonDuration = 34f;
    [Tooltip("Progress along the ride at which the season change BEGINS (before this it stays summer).")]
    [Range(0f, 1f)] public float startProgress = 0.6f;

    [Header("Terrains")]
    [Tooltip("Every terrain to recolour. Left empty, ALL terrains in the scene are used.")]
    public Terrain[] terrains;
    [Tooltip("Legacy single-terrain field — folded into 'terrains'.")]
    public Terrain terrain;

    [Tooltip("OFF by default: the terrain's own layers aren't these textures, so swapping them repaints the ground with the wrong texture.")]
    public bool swapGroundTexture = false;
    public Texture2D summerGround, autumnGround, winterGround;

    [Header("Grass (detail) tint")]
    public Color grassAutumn = new Color(0.72f, 0.5f, 0.24f);
    public Color grassWinter = new Color(0.92f, 0.95f, 1.0f);
    [Range(0f, 1f)] public float autumnBlend = 0.7f;
    [Range(0f, 1f)] public float winterBlend = 0.9f;
    [Tooltip("DO NOT enable. Turning instancing off makes the terrain apply healthyColor/dryColor — but instanced prototypes leave those at WHITE because they are unused, so the grass turns white in summer. Kept only as an escape hatch.")]
    public bool forceTintableDetails = false;
    [Tooltip("How instanced grass is actually recoloured: each detail prototype prefab is cloned at runtime and ITS material is tinted. The prefab asset is never touched.")]
    public bool tintDetailMaterials = true;

    [Header("Season prefab variants (matched by 'Setup Act II Seasons')")]
    [Tooltip("Original prototype prefab (tree or detail) — the key.")]
    public GameObject[] variantBase;
    public GameObject[] variantAutumn;
    public GameObject[] variantWinter;

    // When true the sequence director drives the look via ApplyU().
    [HideInInspector] public bool manual = false;
    public void ApplyU(float u) { if (_ready) ApplyProgress(Mathf.Clamp01(u)); }

    private class TState
    {
        public Terrain terrain;
        public TerrainData orig, work;
        public TerrainCollider collider;
        public Color[] baseHealthy, baseDry;
        public GameObject[] origDetail, origTree;
        // Runtime clones of the detail prototype prefabs, and the material
        // instances on them we tint (instanced grass draws the prefab's
        // material and ignores healthyColor/dryColor entirely).
        // THREE pre-tinted clones per detail prototype, one per season. Mutating
        // one clone's material did not take: the terrain snapshots a prototype
        // when it is registered, and even RefreshPrototypes did not pick the new
        // colour up. Swapping the prototype OBJECT is a change it cannot miss.
        public GameObject[][] seasonClones;   // [season][prototypeIndex]
    }

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");

    private readonly List<TState> _states = new List<TState>();
    private int _protoState = -1;
    private int _texState = -1;
    private float _lastBlend = -1f;
    private float _clock;
    private bool _ready;

    private void OnEnable()
    {
        _states.Clear();

        var list = new List<Terrain>();
        if (terrains != null) foreach (var t in terrains) if (t != null && !list.Contains(t)) list.Add(t);
        if (terrain != null && !list.Contains(terrain)) list.Add(terrain);
        if (list.Count == 0)
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && !list.Contains(t)) list.Add(t);

        foreach (var t in list)
        {
            if (t == null || t.terrainData == null) continue;
            var s = new TState { terrain = t, orig = t.terrainData };
            s.work = Instantiate(s.orig);
            s.work.name = s.orig.name + " (TrailerClone)";
            t.terrainData = s.work;
            s.collider = t.GetComponent<TerrainCollider>();
            if (s.collider != null) s.collider.terrainData = s.work;

            var det = s.work.detailPrototypes;
            s.baseHealthy = new Color[det.Length];
            s.baseDry = new Color[det.Length];
            s.origDetail = new GameObject[det.Length];
            bool touched = false;
            for (int i = 0; i < det.Length; i++)
            {
                s.baseHealthy[i] = det[i].healthyColor;
                s.baseDry[i] = det[i].dryColor;
                s.origDetail[i] = det[i].prototype;
                if (forceTintableDetails && det[i].useInstancing)
                {
                    // Instanced details sample the prefab's material and ignore the
                    // healthy/dry colours — that's why the painted grass stayed green.
                    det[i].useInstancing = false;
                    if (det[i].usePrototypeMesh) det[i].renderMode = DetailRenderMode.VertexLit;
                    touched = true;
                }
            }
            if (touched) s.work.detailPrototypes = det;

            if (tintDetailMaterials) BuildDetailTintClones(s);

            var tp = s.work.treePrototypes;
            s.origTree = new GameObject[tp.Length];
            for (int i = 0; i < tp.Length; i++) s.origTree[i] = tp[i].prefab;

            _states.Add(s);
        }

        _texState = -1; _protoState = -1; _lastBlend = -1f; _clock = 0f;
        _ready = _states.Count > 0;
        if (_ready) ApplyProgress(0f);
    }

    private void OnDisable() { Restore(); }
    private void OnDestroy() { Restore(); }

    // Instanced painted grass draws the PREFAB's material, so healthyColor /
    // dryColor do nothing (that is why the grass never changed). Clone each
    // prototype prefab, give the clone its own material instances, and point the
    // terrain at the clone — then the season tint is just a colour write. The
    // prefab assets and their shared materials are never touched.
    private void BuildDetailTintClones(TState s)
    {
        var det = s.work.detailPrototypes;
        s.seasonClones = new GameObject[3][];
        for (int season = 0; season < 3; season++) s.seasonClones[season] = new GameObject[det.Length];

        for (int i = 0; i < det.Length; i++)
        {
            var src = det[i].prototype;
            if (src == null) continue;                       // texture grass → healthy/dry tint works

            for (int season = 0; season < 3; season++)
            {
                var clone = Instantiate(src);
                clone.name = $"{src.name} (Trailer{(season == 0 ? "Summer" : season == 1 ? "Autumn" : "Winter")})";
                clone.hideFlags = HideFlags.HideAndDontSave;
                clone.SetActive(false);
                clone.transform.SetParent(transform, false);

                if (season > 0)
                {
                    Color tint = season == 1 ? grassAutumn : grassWinter;
                    float blend = season == 1 ? autumnBlend : winterBlend;
                    foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = r.sharedMaterials;
                        for (int m = 0; m < mats.Length; m++)
                        {
                            if (mats[m] == null) continue;
                            var inst = new Material(mats[m]) { hideFlags = HideFlags.HideAndDontSave };
                            Color bc = inst.HasProperty(BaseColorID) ? inst.GetColor(BaseColorID)
                                     : inst.HasProperty(ColorID) ? inst.GetColor(ColorID) : Color.white;
                            Color c = Color.Lerp(bc, tint, blend); c.a = bc.a;
                            if (inst.HasProperty(BaseColorID)) inst.SetColor(BaseColorID, c);
                            if (inst.HasProperty(ColorID)) inst.SetColor(ColorID, c);
                            mats[m] = inst;
                        }
                        r.sharedMaterials = mats;
                    }
                }
                s.seasonClones[season][i] = clone;
            }
        }
        int built = 0;
        foreach (var arr in s.seasonClones) if (arr != null) foreach (var g in arr) if (g != null) built++;
        // A WARNING, not a log: three attempts have been spent recolouring the
        // wrong thing, and this line is what says which mechanism actually
        // applies. It has to be impossible to miss in a filtered console.
        var names = new List<string>();
        foreach (var d in det)
            names.Add(d.usePrototypeMesh
                ? $"MESH:{(d.prototype != null ? d.prototype.name : "null")}"
                : $"TEXTURE:{(d.prototypeTexture != null ? d.prototypeTexture.name : "null")}");
        var layers = new List<string>();
        foreach (var l in s.work.terrainLayers) layers.Add(l != null ? l.name : "null");

        Debug.LogWarning($"[Trailer] GRASS on '{s.terrain.name}': {det.Length} detail prototype(s) [{string.Join(", ", names)}], " +
                         $"{built} season clone(s) built. Terrain LAYERS (the painted ground texture): [{string.Join(", ", layers)}]. " +
                         "MESH prototypes recolour by clone swap; TEXTURE prototypes by healthy/dry tint; the painted ground itself is a terrain LAYER and neither touches it.");
        ApplySeasonClones(s, 0);
    }

    // Point every mesh detail prototype at that season's pre-tinted clone.
    private void ApplySeasonClones(TState s, int season)
    {
        if (s.seasonClones == null) return;
        var det = s.work.detailPrototypes;
        bool changed = false;
        for (int i = 0; i < det.Length && i < s.seasonClones[season].Length; i++)
        {
            var want = s.seasonClones[season][i];
            if (want == null || det[i].prototype == want) continue;
            det[i].prototype = want;
            changed = true;
        }
        if (!changed) return;
        s.work.detailPrototypes = det;
        s.work.RefreshPrototypes();
        if (s.terrain != null) s.terrain.Flush();
        Debug.Log($"[Trailer] Grass season {season} applied on '{s.terrain.name}' ({det.Length} detail prototype(s)).");
    }



    private void Restore()
    {
        foreach (var s in _states)
        {
            if (s.terrain != null && s.orig != null) s.terrain.terrainData = s.orig;
            if (s.collider != null && s.orig != null) s.collider.terrainData = s.orig;
            if (s.work != null) Destroy(s.work);
            if (s.seasonClones != null)
                foreach (var arr in s.seasonClones)
                    if (arr != null) foreach (var g in arr) if (g != null) Destroy(g);
        }
        _states.Clear();
        _ready = false;
    }

    private void Update()
    {
        if (!_ready || manual) return;
        float raw = (driveByRideProgress && ride != null)
            ? Mathf.Clamp01(ride.progress01)
            : (seasonDuration > 0.01f ? Mathf.Clamp01((_clock += Time.deltaTime) / seasonDuration) : 0f);
        ApplyProgress(Mathf.InverseLerp(startProgress, 1f, raw));
    }

    private void ApplyProgress(float u)
    {
        int season = u < 0.4f ? 0 : (u < 0.72f ? 1 : 2);
        if (season != _protoState)
        {
            SwapPrototypes(season);
            foreach (var st in _states) ApplySeasonClones(st, season);
            _protoState = season;
        }

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

        // Detail refresh is heavy — only push when it changed enough.
        if (Mathf.Abs(blend - _lastBlend) > 0.03f)
        {
            foreach (var s in _states)
            {
                // Texture / vertex-lit grass: the healthy/dry colours DO apply.
                // Only touch prototypes we did NOT clone, so instanced ones keep
                // their untouched (white, unused) values instead of turning the
                // summer grass white.
                var det = s.work.detailPrototypes;
                bool wrote = false;
                for (int i = 0; i < det.Length && i < s.baseHealthy.Length; i++)
                {
                    bool cloned = s.seasonClones != null && s.seasonClones[0] != null
                               && i < s.seasonClones[0].Length && s.seasonClones[0][i] != null;
                    if (cloned) continue;
                    det[i].healthyColor = Color.Lerp(s.baseHealthy[i], tint, blend);
                    det[i].dryColor = Color.Lerp(s.baseDry[i], tint, blend);
                    wrote = true;
                }
                if (wrote)
                {
                    s.work.detailPrototypes = det;
                    if (s.terrain != null) s.terrain.Flush();
                }
            }
            _lastBlend = blend;
        }

        if (swapGroundTexture)
        {
            int want = season;
            if (want != _texState)
            {
                var tex = want == 0 ? summerGround : (want == 1 ? autumnGround : winterGround);
                if (tex != null)
                {
                    foreach (var s in _states)
                    {
                        var layers = s.work.terrainLayers;
                        if (layers == null || layers.Length == 0 || layers[0] == null) continue;
                        var clone = Instantiate(layers[0]);
                        clone.diffuseTexture = tex;
                        layers[0] = clone;
                        s.work.terrainLayers = layers;
                    }
                }
                _texState = want;
            }
        }
    }

    // Terrain trees and painted mesh grass are drawn from PROTOTYPES — recolouring
    // them means pointing each prototype at its season-variant prefab.
    private void SwapPrototypes(int season)
    {
        foreach (var s in _states)
        {
            var tp = s.work.treePrototypes;
            bool changed = false;
            for (int i = 0; i < tp.Length && i < s.origTree.Length; i++)
            {
                var want = Variant(s.origTree[i], season);
                if (tp[i].prefab != want) { tp[i].prefab = want; changed = true; }
            }
            if (changed) s.work.treePrototypes = tp;

            var det = s.work.detailPrototypes;
            bool dChanged = false;
            for (int i = 0; i < det.Length && i < s.origDetail.Length; i++)
            {
                if (s.origDetail[i] == null) continue;   // texture grass → the tint handles it
                // Cloned prototypes are season-driven by their material tint —
                // swapping them back to the source prefab would undo it.
                if (s.seasonClones != null && s.seasonClones[0] != null
                    && i < s.seasonClones[0].Length && s.seasonClones[0][i] != null) continue;
                var want = Variant(s.origDetail[i], season);
                if (det[i].prototype != want) { det[i].prototype = want; dChanged = true; }
            }
            if (dChanged) s.work.detailPrototypes = det;

            if (changed || dChanged)
            {
                s.work.RefreshPrototypes();
                if (s.terrain != null) s.terrain.Flush();
            }
        }
    }

    private GameObject Variant(GameObject basePrefab, int season)
    {
        if (basePrefab == null || season == 0 || variantBase == null) return basePrefab;
        for (int i = 0; i < variantBase.Length; i++)
        {
            if (variantBase[i] != basePrefab) continue;
            var v = season == 1
                ? (variantAutumn != null && i < variantAutumn.Length ? variantAutumn[i] : null)
                : (variantWinter != null && i < variantWinter.Length ? variantWinter[i] : null);
            return v != null ? v : basePrefab;
        }
        return basePrefab;
    }

    private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}
