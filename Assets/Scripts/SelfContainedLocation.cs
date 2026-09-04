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

    [Tooltip("Extra margin (m) around the footprint, used for the vegetation exclusion only.")]
    public float margin = 4f;

    [Tooltip("Punch a hole in the procedural terrain under this location. Turn off if you'd rather keep the generated ground (e.g. the location has a transparent/no floor of its own).")]
    public bool cutTerrainHole = true;

    [Tooltip("Metres the hole is pulled IN from the footprint edge. The hole used to be cut at footprint + margin — always LARGER than the location — so a ring of terrain vanished around it and the world water showed through as a pit. Cutting it smaller lets the location's own ground overlap the seam. Only lower this if you can see procedural ground poking through the floor.")]
    public float holeInset = 6f;

    [Header("Plateau (raise the land under the location)")]
    [Tooltip("Raise the terrain into a hill with a flat top under this location, instead of dropping it on the flat. The generated roads climb the slope to reach it, so a castle reads as commanding the valley.")]
    public bool raiseHill = false;

    [Tooltip("How high above the surrounding land the plateau top sits (m).")]
    public float hillHeight = 22f;

    [Tooltip("How far the slope runs out from the footprint edge (m). Longer = gentler climb; keep it well above hillHeight or the sides become cliffs the horse and enemies can't walk up.")]
    public float hillSlopeLength = 90f;

    [Header("Own ground")]
    [Tooltip("Drag the location's OWN ground mesh here (its 'Terrain' child). The terrain hole is cut to match ITS real bounds, so the procedural ground is removed exactly where this covers it and nowhere else. Left empty, a child named terrain/ground is used, then the root BoxCollider.")]
    public Transform groundReference;

    [Header("Water alignment")]
    [Tooltip("Drag the location's WATER object here. If set and 'alignWaterToWorld' is on, the whole location is shifted vertically so its water sits exactly on the world's water plane — so the generated world water and the location's water are one continuous level.")]
    public Transform waterReference;

    [Tooltip("Shift the whole location so its waterReference lands on the world water plane (instead of grounding by the root BoxCollider). Use this when the location brings its own water and you want it to merge with the generated water level.")]
    public bool alignWaterToWorld = false;
}
