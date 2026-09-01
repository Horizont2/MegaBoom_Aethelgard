using System.Collections;
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
        // rests on the terrain (highest ground point under its footprint, so it
        // never sinks into a rise). Requires a BoxCollider on this root.
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null)
        {
            Bounds b = box.bounds;
            float boxBottom = b.min.y;
            float highestGround = float.NegativeInfinity;
            for (int ix = 0; ix <= 4; ix++)
            {
                for (int iz = 0; iz <= 4; iz++)
                {
                    Vector3 p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, ix / 4f), b.max.y + 1f,
                                            Mathf.Lerp(b.min.z, b.max.z, iz / 4f));
                    if (TryGround(p, out float g) && g > highestGround) highestGround = g;
                }
            }
            if (highestGround > float.NegativeInfinity)
                transform.position += new Vector3(0f, highestGround - boxBottom, 0f);
        }

        // PHASE 2 — per direct child: lower ONLY the pieces left floating in the
        // air; leave anything that ended up BELOW ground exactly as it is.
        int moved = 0;
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null) continue;
            if (child.GetComponentInParent<NoGroundSnap>() != null) continue;
            if (NameExcluded(child.name)) continue;

            if (!TryMeasure(child, out Vector3 footXZ, out float lowestY)) continue;
            if (!TryGround(footXZ, out float groundY)) continue;

            float gap = lowestY - groundY;                 // >0 floating, <0 sunk
            if (gap <= tolerance) continue;                // sunk or already grounded → leave as-is
            if (gap > maxSnapDistance) continue;           // too far up → assume intentional

            child.position -= new Vector3(0f, gap, 0f);    // drop floater onto the ground
            moved++;
        }

        if (moved > 0)
            Debug.Log($"[LocationGroundSnapper] '{name}': lowered {moved} floating piece(s) to the ground.");
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
        return false;
    }
}
