using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Decides whether a region has a reliquary in it, and where.
//
// ==== SCARCITY IS THE FEATURE ====
//
// The numbers below are small on purpose, and they are the part of this system
// most worth defending. A reliquary is not a collectible to be swept up; it is
// supposed to be the thing you spot on the ridge and change your route for. Put
// six in a region and within two regions the player is walking a circuit, the
// silhouette stops meaning anything, and the armour economy is gone in an
// afternoon.
//
// So most regions have one. Some have none. A legendary site is genuinely rare,
// and even when one exists it still has to be found.
//
// It also PLACES rather than promoting an existing chest. The old version hung
// itself off Camp_POI, which meant the reward sites were wherever the generator
// happened to drop a camp — no control over spacing, over distance from the
// start, or over whether the site could be seen at all.
[DisallowMultipleComponent]
public class ReliquaryDirector : MonoBehaviour
{
    // ==== OFF BY DEFAULT: THE HAND-BUILT LOCATIONS ARE THE CONTENT NOW ====
    //
    // This director exists from before there were any authored reliquary
    // locations. It scatters a generated site on bare ground — banners, a
    // chest, some guardians — which was the only way to have the feature at
    // all back then.
    //
    // Reliq_1/2/3 are now in the generator's POI list with their own rarity,
    // and a composed location beats anything this can assemble from parts. Both
    // running at once is how a region ends up showing the improvised version
    // INSTEAD of the designed one, which is what was reported.
    //
    // The stand-down check below was supposed to prevent that by counting what
    // the POI pass had already placed, but it is a race against the whole POI
    // phase succeeding, and it fails silently and in the wrong direction — the
    // procedural site appears and the authored one does not. Not running unless
    // asked is the honest default. Turn it on only if a region needs sites the
    // POI list cannot supply.
    [Header("How many exist at all")]
    [Tooltip("Scatter GENERATED reliquary sites on open ground. Off by default: the hand-built Reliq locations in the generator's POI list are the real content, and running both means the improvised version can show up instead of the designed one.")]
    public bool enableProceduralSites = false;

    [Tooltip("Chance this region contains any reliquary. Below 1 on purpose: a region with nothing in it is what makes the next one's silhouette worth noticing.")]
    [Range(0f, 1f)] public float regionHasOneChance = 0.9f;
    [Tooltip("Upper bound when the roll succeeds. Two is a lot already.")]
    public int maxPerRegion = 2;
    [Tooltip("Chance a placed site is a guarded Shrine rather than a plain wayside find.")]
    [Range(0f, 1f)] public float shrineChance = 0.40f;
    [Tooltip("Chance a placed site is a Barrow — four guardians, a nine-second hold and a wave. The rarest thing in the system; treat any increase as an economy change, not a tuning tweak.")]
    [Range(0f, 0.4f)] public float barrowChance = 0.12f;

    [Header("Where")]
    [Tooltip("Never nearer the player's start than this — a landmark visible from spawn is not a discovery.")]
    public float minDistanceFromStart = 110f;
    public float minSpacing = 140f;
    [Tooltip("Clear ground needed around the site so the banners are not standing inside a tree.")]
    public float clearRadius = 7f;
    public float edgeMargin = 90f;
    [Tooltip("Metres a site must sit above the water line. Keeps shrines out of lakes AND off the shoreline, where the banners would stand in the shallows.")]
    public float waterClearance = 2.5f;
    [Tooltip("Level the ground under the site. Without it a shrine on any slope has half its stones buried and the chest floating.")]
    public bool flattenGround = true;

    public static int Placed { get; private set; }
    public static int Opened { get; private set; }

    private static readonly List<Reliquary> s_live = new List<Reliquary>(4);
    private static readonly Collider[] s_clearance = new Collider[32];

    private void Start() => StartCoroutine(PlaceWhenWorldExists());

    private IEnumerator PlaceWhenWorldExists()
    {
        // One frame for MissionInitializer.Start to publish the region, then ask.
        yield return null;

        // NOT gated on region mode any more.
        //
        // It used to bail out unless PlayerPrefs said "IsRegionMission" — which is
        // 0 whenever the scene is played straight from the editor, so every test
        // run produced a world with no exploration in it at all and one quiet log
        // line to say why. This director only ever runs because a WorldGenerator
        // finished building a world, and a generated world is a world worth
        // exploring, whichever mission put it there.
        if (!enableProceduralSites)
        {
            Debug.Log("[Reliquary] Procedural sites are off — the hand-built Reliq locations in the generator's " +
                      "POI list are the source of reliquaries. See ReliquaryDirector.enableProceduralSites.");
            yield break;
        }

        if (!WorldEncounterDirector.IsAnyRegionMode())
            Debug.Log("[Reliquary] Not flagged as a region mission — placing anyway, since a world was generated.");

        // Sites are chosen against the finished terrain and the finished tree
        // cover, so this waits for a definite signal rather than a guessed number
        // of frames — the failure mode here is silence.
        float deadline = Time.time + 90f;
        while (!WorldGenerator.IsGenerationDone && Time.time < deadline) yield return null;
        if (!WorldGenerator.IsGenerationDone)
            Debug.LogWarning("[Reliquary] World generation never reported done — placing against whatever terrain exists.");
        yield return null;

        Place();
    }

    private void Place()
    {
        s_live.Clear();
        Placed = 0;
        Opened = 0;

        var set = ReliquarySet.Load();
        if (set == null || !set.IsUsable)
        {
            Debug.LogWarning("[Reliquary] Set missing or has no chest prefab — nothing placed.");
            return;
        }

        // Per-region overrides win where a designer set them. The values here are
        // the house default, not the law: an early forest and the Throne Room
        // should not be seeded the same way, and forcing that decision through
        // one global number is how a system stops being tunable.
        RegionData region = GameManager.Instance != null ? GameManager.Instance.currentRegion : null;
        if (region == null) region = MissionInitializer.PendingMissionRegion;

        float chance = region != null && region.reliquaryChance >= 0f ? region.reliquaryChance : regionHasOneChance;
        int cap = region != null && region.maxReliquaries >= 0 ? region.maxReliquaries : maxPerRegion;
        float pShrine = region != null && region.shrineChance >= 0f ? region.shrineChance : shrineChance;
        float pBarrow = region != null && region.barrowChance >= 0f ? region.barrowChance : barrowChance;

        if (Random.value > chance)
        {
            Debug.Log($"[Reliquary] This region has none (chance {chance:P0}). That is by design — see ReliquaryDirector.");
            return;
        }

        Vector3 start = Vector3.zero;
        var pc = FindFirstObjectByType<PlayerController>();
        if (pc != null) start = pc.transform.position;

        // STAND DOWN IF THE MAP ALREADY HAS SITES.
        //
        // Hand-built locations carrying a reliquary now go in the generator's POI
        // list with their own rarity, which is the better route — a composed
        // location beats anything this can scatter on bare ground. But both
        // systems running blind to each other is how a region ends up with five
        // of them and the scarcity that the whole economy rests on quietly
        // evaporates. Whatever the POI pass placed counts against the budget.
        var already = FindObjectsByType<Reliquary>(FindObjectsSortMode.None);
        int fromLocations = already != null ? already.Length : 0;

        int want = Random.Range(1, Mathf.Max(1, cap) + 1) - fromLocations;
        if (want <= 0)
        {
            Debug.Log($"[Reliquary] {fromLocations} already placed by hand-built POI locations — " +
                      "the director is standing down rather than doubling up.");
            Placed = fromLocations;
            return;
        }
        if (fromLocations > 0)
            Debug.Log($"[Reliquary] {fromLocations} came from POI locations; placing {want} more on open ground.");

        var taken = new List<Vector3>(want);
        foreach (var r in already) if (r != null) taken.Add(r.transform.position);

        // The search RELAXES rather than failing.
        //
        // This is why nothing was ever placed on a real map. The first pass wants
        // seven metres of clear ground a hundred and ten metres from the player on
        // a slope of under three metres — perfectly reasonable numbers, and on a
        // dense forested region there is often nowhere on the whole terrain that
        // satisfies all of them at once. Four hundred rejections later the
        // director logged a warning nobody read and the feature simply did not
        // exist that run.
        //
        // Now each block of attempts loosens the constraints, and the last block
        // asks only for dry land. A site that is slightly cramped is enormously
        // better than no site, and the numbers at the top still describe the
        // sites we PREFER — they just no longer describe the only ones allowed.
        for (int attempt = 0; attempt < 900 && taken.Count < want; attempt++)
        {
            float ease = attempt < 300 ? 1f : attempt < 600 ? 0.6f : 0.25f;
            if (!TryFindSite(start, taken, ease, out Vector3 site)) continue;

            float roll = Random.value;
            var rollGrade = roll < pBarrow ? Reliquary.Grade.Barrow
                          : roll < pBarrow + pShrine ? Reliquary.Grade.Shrine
                          : Reliquary.Grade.Wayside;

            // Level the ground FIRST, before a single prop is placed.
            //
            // Everything the site builds samples the terrain to sit on it — the
            // banners, the stones, the chest, the guardians. Flattening
            // afterwards would move the ground out from under all of them and
            // leave the whole shrine buried or hovering. The site is also
            // re-sampled after this so the chest sits on the new level, not the
            // old slope.
            if (flattenGround) site = LevelGround(site, rollGrade);

            // The site is a PREFAB now, not something assembled here.
            //
            // Everything that used to be built at runtime — measuring the chest
            // model, scaling it, grounding it, bolting on an Animator — is baked
            // into the asset by Tools > Exploration > Build Reliquary Prefabs,
            // where it is visible and fixable. Two things stopped being possible
            // the moment that moved: a chest coming out the wrong size, and a lid
            // with no animator behind it.
            var prefab = set.SiteFor((int)rollGrade);
            if (prefab == null)
            {
                Debug.LogWarning("[Reliquary] The ReliquarySet has no site prefabs. Run " +
                                 "Tools > Exploration > Build Reliquary Prefabs — nothing can be placed without them.");
                break;
            }

            var go = Instantiate(prefab, site, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            // Named by grade so a hierarchy search for "Reliquary" both finds
            // them and says what each one is without clicking it.
            go.name = $"Reliquary_{rollGrade}_{taken.Count}";

            var rel = go.GetComponent<Reliquary>();
            if (rel == null) { Destroy(go); continue; }
            rel.grade = rollGrade;
            // Bare ground: this one dresses itself. A prefab dropped into a
            // hand-built location leaves the dressing to the location.
            rel.buildDecor = true;

            // Distance from the start is the only honest way to pay for a walk.
            float d = Vector3.Distance(site, start);
            rel.richness = Mathf.Lerp(1f, 2.1f, Mathf.InverseLerp(minDistanceFromStart, minDistanceFromStart + 260f, d));

            taken.Add(site);
            s_live.Add(rel);
        }

        Placed = s_live.Count;
        if (Placed == 0)
            Debug.LogWarning($"[Reliquary] Wanted {want} but found no site with {clearRadius}m of clear ground " +
                             $"at least {minDistanceFromStart}m from the player. Lower clearRadius on a dense map.");
        else
        {
            var where = new System.Text.StringBuilder();
            foreach (var r in s_live)
                where.Append($"\n    {r.grade} at {r.transform.position}  ({Vector3.Distance(r.transform.position, start):F0}m from start)");
            Debug.Log($"[Reliquary] Placed {Placed} " +
                      $"({s_live.FindAll(r => r.grade == Reliquary.Grade.Barrow).Count} barrow, " +
                      $"{s_live.FindAll(r => r.grade == Reliquary.Grade.Shrine).Count} shrine). " +
                      $"Lifetime armour granted: {ArmourLootTable.LifetimeFound}." + where);
        }
    }

    // Flatten a pad for the site and hand back the settled ground position.
    //
    // Uses the generator's own FlattenTerrainRobust rather than a second
    // implementation — the roads and the hand-built locations already level
    // ground with it, and two routines that flatten "almost the same way" is how
    // a seam appears where they meet. A barrow needs a wider pad than a wayside
    // find because its ring of props is wider; the falloff blends it back into
    // the hillside so the pad does not read as a plateau someone stamped out.
    private Vector3 LevelGround(Vector3 site, Reliquary.Grade grade)
    {
        var gen = FindFirstObjectByType<WorldGenerator>();
        if (gen == null) return site;

        // Wider than the site itself. The pad has to cover the ground the
        // guardians stand on and the vigil is fought over, not just the footprint
        // of the chest — a shrine on a level disc surrounded by a slope is a
        // shrine you fight on a slope.
        float radius = grade switch { Reliquary.Grade.Barrow => 14f, Reliquary.Grade.Shrine => 11f, _ => 7f };
        gen.FlattenTerrainRobust(site, radius, radius * 2f, site.y);

        // Re-sample: SetHeights has just moved the surface, and every prop about
        // to be placed will raycast against the NEW one.
        Terrain t = Terrain.activeTerrain;
        if (t != null) site.y = t.SampleHeight(site) + t.transform.position.y;
        return site;
    }

    // `ease` is 1 for the preferred constraints and drops towards 0 as the search
    // gives up on finding a perfect spot. See the comment at the call site: this
    // is the difference between the feature existing on a dense map and not.
    private bool TryFindSite(Vector3 start, List<Vector3> taken, float ease, out Vector3 site)
    {
        site = Vector3.zero;

        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null || terrain.terrainData == null) return false;
        Vector3 o = terrain.transform.position;
        Vector3 s = terrain.terrainData.size;

        // THE EDGE MARGIN IS NOT RELAXED. This is the bug that put every
        // reliquary on the rim of the map.
        //
        // Relaxing it looked like just another constraint to loosen, but it is
        // the one that runs backwards: the interior is full of trees, rocks and
        // locations, so it fails the clearance test, while the outer band is
        // empty precisely BECAUSE the generator does not dress it. Widening the
        // search outward therefore does not find more places, it finds the only
        // places left — a ring of bare ground around the edge of the world,
        // which is the least interesting spot on the map and the furthest from
        // anywhere the player has a reason to be.
        //
        // So the margin holds, and everything else gives instead.
        float margin = Mathf.Min(edgeMargin, Mathf.Min(s.x, s.z) * 0.35f);
        Vector3 p = new Vector3(
            Random.Range(o.x + margin, o.x + s.x - margin),
            0f,
            Random.Range(o.z + margin, o.z + s.z - margin));
        p.y = terrain.SampleHeight(p) + o.y;

        float minStart = minDistanceFromStart * ease;
        // Pull candidates toward the middle. Two uniform draws averaged is a
        // triangular distribution — cheap, and it puts the density where the
        // world is actually built rather than spread evenly out to the border.
        p.x = (p.x + Random.Range(o.x + margin, o.x + s.x - margin)) * 0.5f;
        p.z = (p.z + Random.Range(o.z + margin, o.z + s.z - margin)) * 0.5f;
        p.y = terrain.SampleHeight(p) + o.y;

        if ((p - start).sqrMagnitude < minStart * minStart) return false;
        float spacing = minSpacing * ease;
        foreach (var t in taken)
            if ((p - t).sqrMagnitude < spacing * spacing) return false;

        // NOT IN WATER. Rivers and lakes are carved into the same heightmap this
        // samples, so a site chosen on height alone lands happily on a lake bed —
        // and a shrine at the bottom of a river is both unreachable and absurd.
        // The margin keeps it off the shoreline too, where the banners would
        // stand in the shallows.
        var gen = FindFirstObjectByType<WorldGenerator>();
        if (gen != null && p.y < gen.AbsoluteWaterHeight + waterClearance) return false;

        // Room for the banners, and flat enough that a shrine does not end up
        // half-buried in a slope.
        //
        // TerrainColliders are skipped explicitly, and that is not a detail: a
        // sphere of this radius sitting a metre above the ground ALWAYS
        // intersects the terrain it is standing on, so a plain CheckSphere
        // rejected every candidate site and no reliquary was ever placed
        // anywhere. The test is "is anything built or grown here", not "is there
        // ground here" — there had better be ground here.
        float clear = Mathf.Max(2.5f, clearRadius * ease);
        int n = Physics.OverlapSphereNonAlloc(p + Vector3.up * 1.5f, clear, s_clearance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (s_clearance[i] == null) continue;
            if (s_clearance[i] is TerrainCollider) continue;
            return false;
        }

        float h1 = terrain.SampleHeight(p + new Vector3(clear, 0f, 0f)) + o.y;
        float h2 = terrain.SampleHeight(p + new Vector3(-clear, 0f, 0f)) + o.y;
        float h3 = terrain.SampleHeight(p + new Vector3(0f, 0f, clear)) + o.y;
        float h4 = terrain.SampleHeight(p + new Vector3(0f, 0f, -clear)) + o.y;
        float spread = Mathf.Max(Mathf.Max(h1, h2), Mathf.Max(h3, h4)) - Mathf.Min(Mathf.Min(h1, h2), Mathf.Min(h3, h4));
        if (spread > 3.2f / Mathf.Max(0.2f, ease)) return false;

        site = p;
        return true;
    }

    public static void NoteOpened(Reliquary rel)
    {
        if (rel == null || !s_live.Contains(rel)) return;
        s_live.Remove(rel);
        Opened++;
    }

    // Statics outlive a scene load; without this the next region believes its
    // reliquaries are already spent.
    public static void Reset()
    {
        s_live.Clear();
        Placed = 0;
        Opened = 0;
    }

    // Called by WorldGenerator once the world exists.
    //
    // NOT from RuntimeInitializeOnLoadMethod, which was the bug: that fires ONCE
    // per play session, in whatever scene starts first. The director was built
    // in the menu, found no region, and was never built again when the game
    // scene loaded — so no reliquary ever existed anywhere, and there was no log
    // saying why because the code that would log it never ran.
    //
    // WorldGenerator installs the ambient birds the same way, and those work.
    public static void Install()
    {
        if (FindFirstObjectByType<ReliquaryDirector>() != null) return;
        Reset();
        new GameObject("[Reliquaries]").AddComponent<ReliquaryDirector>();
        // Logged synchronously, at the moment of creation.
        //
        // Every previous round of "they still are not spawning" was impossible to
        // diagnose from the outside because silence meant two completely
        // different things: the code did not run, or it ran and bailed. This line
        // separates them. If it is absent from the console, nothing here executed
        // at all and the problem is upstream of this file.
        Debug.Log("[Reliquary] Director installed by WorldGenerator — placement decision follows.");
    }
}
