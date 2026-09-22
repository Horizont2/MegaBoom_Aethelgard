using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Drop this on a LOCATION ROOT (a town / castle / camp prefab you placed by
// hand in the editor). After the world terrain is generated, it walks the
// location's direct children and, for any piece that ended up slightly ABOVE or
// BELOW the ground (a misaligned prop from editor placement), snaps it so its
// lowest point rests on the terrain.
//
// It runs once, when WorldGenerator fires OnWorldGenerationComplete, so it
// measures against the FINAL carved/flattened terrain (not the pre-generation
// scene). If the world is already generated when this enables, it snaps next
// frame.
//
// EXCLUSIONS (never moved):
//   * anything carrying a NoGroundSnap component (on itself or a parent),
//   * anything whose name contains one of `excludeNameContains` — by default
//     trees and water-mills / waterwheels, which are meant to float over water
//     or sit at an authored height.
// Only corrections within `maxSnapDistance` are applied, so a deliberately
// high element (a bird, a floating crystal) isn't yanked to the floor.
public class LocationGroundSnapper : MonoBehaviour
{
    [Tooltip("Max vertical correction (metres). Pieces further off than this are left alone — they're assumed intentional, not a small editor slip.")]
    public float maxSnapDistance = 8f;

    [Tooltip("Don't bother moving a piece already within this many metres of the ground.")]
    public float tolerance = 0.05f;

    [Tooltip("A piece is skipped if its name contains any of these (case-insensitive). Trees and water-mills are excluded by default.")]
    public string[] excludeNameContains =
        { "tree", "дерев", "bush", "кущ", "mill", "млин", "wheel", "колес", "water", "водян" };

    private bool _done;

    private void OnEnable()
    {
        WorldGenerator.OnWorldGenerationComplete += Run;
        if (WorldGenerator.IsGenerationDone) StartCoroutine(DeferredRun());
    }

    private void OnDisable()
    {
        WorldGenerator.OnWorldGenerationComplete -= Run;
    }

    private IEnumerator DeferredRun()
    {
        yield return null;
        yield return new WaitForFixedUpdate();
        Run();
    }

    [ContextMenu("Snap Location To Ground Now")]
    public void Run()
    {
        if (_done) return;
        _done = true;
        WorldGenerator.OnWorldGenerationComplete -= Run;

        // PHASE 1 — descend the WHOLE location until the root BoxCollider bottom
        // rests on the terrain under its footprint, high enough that it never
        // sinks into a rise. Requires a BoxCollider on this root.
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null)
        {
            Bounds b = box.bounds;
            float boxBottom = b.min.y;

            var samples = new List<float>();
            for (int ix = 0; ix <= 4; ix++)
            {
                for (int iz = 0; iz <= 4; iz++)
                {
                    Vector3 p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, ix / 4f), b.max.y + 1f,
                                            Mathf.Lerp(b.min.z, b.max.z, iz / 4f));
                    if (TryGround(p, out float g)) samples.Add(g);
                }
            }

            // ==== A HIGH SAMPLE, NOT THE HIGHEST ONE ====
            //
            // This took the maximum, on the reasoning that a location must never
            // sink into a rise. True, and it also hands the whole building to a
            // single sample: anything that levels a pad inside the footprint
            // after the location was grounded — a reliquary flattening fourteen
            // metres of terrain in the courtyard — raises exactly one of the
            // twenty-five, and the entire location goes up with it.
            //
            // The ninetieth percentile keeps the intent and drops the outlier.
            // Three of twenty-five samples may sit above the chosen height, so a
            // genuine slope still lifts the location, while one anomalous pad
            // cannot.
            if (samples.Count > 0)
            {
                samples.Sort();
                int at = Mathf.Clamp(Mathf.FloorToInt((samples.Count - 1) * 0.9f), 0, samples.Count - 1);
                transform.position += new Vector3(0f, samples[at] - boxBottom, 0f);
            }
        }

        // PHASE 2 — EVERY PIECE, not every direct child.
        //
        // This walked transform.GetChild(i) and stopped there, which is the
        // right shape for a location assembled out of a dozen props and the
        // wrong one for this castle: it is nineteen hundred nested prefab
        // instances, so its direct children are a handful of GROUPS. Measuring a
        // group gives the bounds of everything in it, and the lowest point of
        // fifty walls is almost always already on the ground — so the gap came
        // out at nothing, the group was left alone, and every floating piece
        // inside it stayed floating.
        //
        // So it descends to the pieces that actually carry geometry, and snaps
        // each one on its own. A transform whose ancestor has already been
        // snapped is skipped, or a piece moves twice.
        var pieces = new List<Transform>();
        Collect(transform, pieces);

        // Shallowest first, so the ancestor test below sees them in the order
        // they can actually shadow each other.
        pieces.Sort((a, b) => Depth(a).CompareTo(Depth(b)));

        var snapped = new List<Transform>();
        int moved = 0;

        foreach (Transform piece in pieces)
        {
            if (piece == null) continue;
            if (HasSnappedAncestor(piece, snapped)) continue;
            if (piece.GetComponentInParent<NoGroundSnap>() != null) continue;
            if (NameExcluded(piece.name)) continue;

            if (!TryMeasure(piece, out Vector3 footXZ, out float lowestY)) continue;
            if (!TryGround(footXZ, out float groundY)) continue;

            float gap = lowestY - groundY;                 // >0 floating, <0 sunk
            if (gap <= tolerance) continue;                // sunk or already grounded → leave as-is
            if (gap > maxSnapDistance) continue;           // too far up → assume intentional

            piece.position -= new Vector3(0f, gap, 0f);    // drop floater onto the ground
            snapped.Add(piece);
            moved++;
        }

        if (moved > 0)
            Debug.Log($"[LocationGroundSnapper] '{name}': lowered {moved} floating piece(s) to the ground.");
    }

    // Every transform that carries geometry of its own. A dressed location nests
    // its props several deep, and it is the props that float — not the folders
    // they are filed under.
    private static void Collect(Transform root, List<Transform> into)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform c = root.GetChild(i);
            if (c == null) continue;

            bool carriesGeometry = false;
            foreach (var r in c.GetComponents<Renderer>())
                if (r != null && r.enabled && !(r is ParticleSystemRenderer)) { carriesGeometry = true; break; }

            if (carriesGeometry) into.Add(c);
            Collect(c, into);
        }
    }

    private static int Depth(Transform t)
    {
        int d = 0;
        while (t.parent != null) { d++; t = t.parent; }
        return d;
    }

    private static bool HasSnappedAncestor(Transform t, List<Transform> snapped)
    {
        for (Transform p = t.parent; p != null; p = p.parent)
            if (snapped.Contains(p)) return true;
        return false;
    }

    // Every point of interest the generator places is parented under one
    // container, which makes the whole family one test rather than a list of
    // component types that grows every time a new kind is added.
    private static bool IsUnderPOIContainer(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
            if (p.name == "POIContainer") return true;
        return false;
    }

    private bool NameExcluded(string n)
    {
        if (excludeNameContains == null) return false;
        string low = n.ToLowerInvariant();
        for (int i = 0; i < excludeNameContains.Length; i++)
        {
            string k = excludeNameContains[i];
            if (!string.IsNullOrEmpty(k) && low.Contains(k.ToLowerInvariant())) return true;
        }
        return false;
    }

    // Combined visible-renderer bounds of a piece → its ground-facing footprint
    // centre (XZ) and its lowest world Y. Particles / disabled renderers ignored.
    private bool TryMeasure(Transform piece, out Vector3 footXZ, out float lowestY)
    {
        footXZ = piece.position;
        lowestY = 0f;
        bool has = false;
        Bounds b = default;
        var rends = piece.GetComponentsInChildren<Renderer>(false);
        foreach (var r in rends)
        {
            if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!has) return false;
        footXZ = new Vector3(b.center.x, b.max.y, b.center.z);
        lowestY = b.min.y;
        return true;
    }

    // Highest terrain/ground hit under an XZ point, ignoring this location's own
    // colliders and any non-ground objects.
    private bool TryGround(Vector3 fromAboveXZ, out float y)
    {
        y = 0f;
        float best = float.NegativeInfinity;
        bool found = false;
        Vector3 origin = new Vector3(fromAboveXZ.x, fromAboveXZ.y + 500f, fromAboveXZ.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 3000f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            if (h.collider == null) continue;
            if (h.collider.transform.IsChildOf(transform)) continue;   // never our own pieces

            // ==== A SHRINE IS NOT GROUND ====
            //
            // Reliquary decor keeps solid colliders on purpose — the stones are
            // cover to fight behind — and the points of interest are the same.
            // So a site that landed inside this location answers the downward
            // ray, and the name test below is generous enough to let the wrong
            // kind of prop through: a road tile is called PP_Floor_Tile, and
            // "floor" is on the list.
            //
            // They are never the floor of a location. Ruled out by what they
            // ARE rather than by what they are called, so a rename cannot bring
            // the bug back.
            if (h.collider.GetComponentInParent<Reliquary>() != null) continue;
            if (IsUnderPOIContainer(h.collider.transform)) continue;

            bool isGround = h.collider.GetComponentInParent<Terrain>() != null;
            if (!isGround)
            {
                string n = h.collider.name.ToLowerInvariant();
                if (n.Contains("terrain") || n.Contains("ground") || n.Contains("road") ||
                    n.Contains("floor") || n.Contains("path")) isGround = true;
            }
            if (!isGround) continue;

            if (h.point.y > best) { best = h.point.y; found = true; }
        }
        if (found) { y = best; return true; }

        // ==== THE HOLE UNDER THE LOCATION ====
        //
        // A self-contained location asks WorldGenerator to punch a hole in the
        // terrain under its footprint, and a terrain hole removes the COLLIDER
        // as well as the surface — so a raycast straight down from inside the
        // footprint hits nothing at all, TryGround fails, and the piece is
        // skipped. Every piece inside the hole was therefore never snapped,
        // which is most of the ones anybody notices.
        //
        // The heightmap is still there underneath: a hole hides the ground, it
        // does not delete the data. Sampling it gives the height the land would
        // have had, which is exactly the height the location was placed to.
        foreach (var t in Terrain.activeTerrains)
        {
            if (t == null || t.terrainData == null) continue;
            Vector3 o = t.transform.position, sz = t.terrainData.size;
            if (fromAboveXZ.x < o.x || fromAboveXZ.x > o.x + sz.x) continue;
            if (fromAboveXZ.z < o.z || fromAboveXZ.z > o.z + sz.z) continue;

            y = t.SampleHeight(fromAboveXZ) + o.y;
            return true;
        }
        return false;
    }
}
