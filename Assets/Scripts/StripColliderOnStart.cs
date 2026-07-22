using UnityEngine;

// Attach to any VFX / decorative prefab that should never block player
// movement. Destroys every Collider under the object on Start so authored
// interaction volumes (used only in the editor tooling of some VFX assets)
// don't leak into runtime as invisible walls.
//
// This is the pragmatic backstop for the "invisible wall in the field"
// bug — the ideal fix is stripping colliders from the source prefabs,
// but with 100+ authored prefabs a runtime scrubber is faster to apply.
[DefaultExecutionOrder(-50)]
public class StripColliderOnStart : MonoBehaviour
{
    // Keep this collider (e.g. a trigger). Everything else under the
    // object is destroyed. Leave null → strip ALL.
    public Collider keepThisOne;
    // Trigger colliders are often needed (pickups, sensors). Keep them
    // by default; only strip solid colliders that block movement.
    public bool keepTriggers = true;

    private void Start()
    {
        Collider[] cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            var c = cols[i];
            if (c == null) continue;
            if (c == keepThisOne) continue;
            if (keepTriggers && c.isTrigger) continue;
            Destroy(c);
        }
    }
}
