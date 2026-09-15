using System.Collections.Generic;
using UnityEngine;

// Picks the footstep sound for whatever the player is standing on.
//
// ==== WHY A CLASSIFIER AND NOT A TAG ON EVERY PROP ====
//
// The obvious implementation is a component on each walkable surface saying
// what it is made of. In a hand-built level that is right. Here the ground is
// generated, the props come from a dozen asset packs nobody in this project
// authored, and anything relying on a component somebody remembered to add ends
// up covering the four objects it was tested against.
//
// So the surface is read from what is already true about the collider: terrain
// versus prop, the physic material an artist did set, and failing that the
// object's own name — asset packs name their meshes after what they are, and
// "Bridge_Planks_02" is a better source of truth here than a component that
// does not exist.
//
// ==== AND WHY THE ANSWER IS CACHED PER COLLIDER ====
//
// This is asked about twice a second for the entire run. Object.name allocates
// a fresh string on every access and the substring scan is not free, so the
// verdict for a given collider is worked out once and remembered. Standing
// still or walking across one surface costs a dictionary lookup.
public static class SurfaceAudio
{
    // Sounds mapped to the surface under the feet. Falls back to the generic
    // footstep for anything unrecognised, which is every surface today — so
    // nothing regresses if a classification is wrong or an event is unauthored.
    public static string FootstepFor(Vector3 feet)
    {
        if (!Physics.Raycast(feet + Vector3.up * 0.6f, Vector3.down, out RaycastHit hit, 2.0f,
                             ~0, QueryTriggerInteraction.Ignore))
            return AudioID.Player_Footstep;

        var col = hit.collider;
        if (col == null) return AudioID.Player_Footstep;

        if (s_verdict.TryGetValue(col, out string cached)) return cached;

        string verdict = Classify(col);
        // Bounded: a long run across a generated region touches a lot of
        // colliders, and this must never become a leak of its own.
        if (s_verdict.Count > 256) s_verdict.Clear();
        s_verdict[col] = verdict;
        return verdict;
    }

    private static readonly Dictionary<Collider, string> s_verdict = new Dictionary<Collider, string>(64);

    private static string Classify(Collider col)
    {
        // The ground itself. Snow in a winter region, ordinary footsteps
        // everywhere else — the generator publishes which one this is.
        if (col is TerrainCollider)
            return WorldGenerator.RegionIsWinter ? AudioID.Player_Footstep_Snow : AudioID.Player_Footstep;

        // An authored physic material beats a guess from a name, every time.
        var pm = col.sharedMaterial;
        if (pm != null)
        {
            string surface = Match(pm.name);
            if (surface != null) return surface;
        }

        string byName = Match(col.name);
        if (byName != null) return byName;

        // One level up: pack meshes are often "Collider" or "Cube" under a
        // parent that carries the real name.
        if (col.transform.parent != null)
        {
            string byParent = Match(col.transform.parent.name);
            if (byParent != null) return byParent;
        }

        return AudioID.Player_Footstep;
    }

    private static string Match(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        string n = raw.ToLowerInvariant();

        if (Has(n, "wood") || Has(n, "plank") || Has(n, "bridge") || Has(n, "deck")
            || Has(n, "log") || Has(n, "timber") || Has(n, "floor_wood"))
            return AudioID.Player_Footstep_Wood;

        if (Has(n, "stone") || Has(n, "rock") || Has(n, "cobble") || Has(n, "paved")
            || Has(n, "pavement") || Has(n, "brick") || Has(n, "marble") || Has(n, "ruin")
            || Has(n, "stair") || Has(n, "cliff"))
            return AudioID.Player_Footstep_Stone;

        if (Has(n, "snow") || Has(n, "ice") || Has(n, "frozen"))
            return AudioID.Player_Footstep_Snow;

        return null;
    }

    private static bool Has(string haystack, string needle) => haystack.IndexOf(needle, System.StringComparison.Ordinal) >= 0;
}
