using UnityEngine;

// Pins a transform to the ground for the rest of the shot.
//
// The rider was landing on grass colliders first and only then settling onto the
// terrain — a visible two-stage fall — and the get-up clip then left his legs
// buried, until the idle popped him back out. The cause is the same in both
// cases: nothing keeps him on the ground between the beats, so whatever the clip
// or the raycast happened to produce is where he stayed.
//
// This clamps the transform's Y to the TERRAIN surface every LateUpdate, after
// the animator has written its pose, so no clip can bury him or leave him
// hovering, and the transition into idle has nothing left to correct.
public class TrailerGroundClamp : MonoBehaviour
{
    [Tooltip("Metres above the terrain surface the pivot sits. 0 for a rig whose pivot is between the feet.")]
    public float footOffset = 0f;

    [Tooltip("Seconds to ease onto the surface when first enabled, so switching it on mid-fall doesn't snap.")]
    public float settleTime = 0.15f;

    [Tooltip("Snap immediately rather than easing — used the moment he lands.")]
    public bool snapNow;

    private float _t;

    private void OnEnable() { _t = 0f; }

    private void LateUpdate()
    {
        if (!TryTerrainY(transform.position, out float groundY)) return;

        float want = groundY + footOffset;
        Vector3 p = transform.position;

        if (snapNow || settleTime <= 0f || _t >= settleTime)
        {
            p.y = want;
        }
        else
        {
            _t += Time.deltaTime;
            p.y = Mathf.Lerp(p.y, want, 1f - Mathf.Exp(-18f * Time.deltaTime));
        }
        transform.position = p;
    }

    // TERRAIN only. Grass, props and debris colliders are exactly what the fall
    // was catching on, so they are not ground for this purpose.
    public static bool TryTerrainY(Vector3 pos, out float y)
    {
        y = pos.y;
        float best = float.NegativeInfinity;
        bool found = false;

        foreach (var t in Terrain.activeTerrains)
        {
            if (t == null || t.terrainData == null) continue;
            Vector3 o = t.transform.position;
            Vector3 size = t.terrainData.size;
            if (pos.x < o.x || pos.x > o.x + size.x || pos.z < o.z || pos.z > o.z + size.z) continue;
            float h = t.SampleHeight(pos) + o.y;
            if (!found || h > best) { best = h; found = true; }
        }

        if (found) { y = best; return true; }
        return false;
    }
}
