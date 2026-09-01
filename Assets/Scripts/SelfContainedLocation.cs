using UnityEngine;

// Put this on a LOCATION prefab that brings its OWN ground + water (e.g. the
// stylized medieval market village with its lakes). When WorldGenerator places
// such a location as a region location, it will:
//   * flatten a pad and drop the prefab so its base sits on that pad,
//   * punch a HOLE in the procedural terrain under the footprint (so the
//     generated ground/collider don't poke through or z-fight the location's
//     own ground), and
//   * keep all procedural trees/rocks/bushes out of the whole footprint.
//
// REQUIREMENTS for it to look/behave right:
//   * the prefab must include its OWN walkable ground collider (its own terrain
//     mesh with a MeshCollider, or a ground plane collider) — the punched hole
//     removes the procedural TerrainCollider there, so without its own collider
//     the player would fall through;
//   * its water sits at its authored local height and is preserved (the placer
//     moves the whole location as one, so the water level relative to its ground
//     never changes).
[DisallowMultipleComponent]
public class SelfContainedLocation : MonoBehaviour
{
    [Tooltip("Footprint radius (m) used for the terrain hole + vegetation exclusion. 0 = auto (uses the flattened pad radius WorldGenerator computed from the prefab bounds).")]
    public float footprintRadius = 0f;

    [Tooltip("Extra margin (m) added around the footprint for the hole + exclusion, so the seam sits just outside the visible ground.")]
    public float margin = 4f;

    [Tooltip("Punch a hole in the procedural terrain under this location. Turn off if you'd rather keep the generated ground (e.g. the location has a transparent/no floor of its own).")]
    public bool cutTerrainHole = true;
}
