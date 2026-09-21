using System.Collections.Generic;
using UnityEngine;

// A single fracture crawling across the surface of the statue.
//
// This is the piece the first version was missing, and its absence is why the
// shot read as fake: light appeared NEXT TO a statue that was visually untouched.
// There was no cause on screen, only an effect, and an audience reads that as
// two unrelated things happening at once rather than as stone failing.
//
// The mesh is a single unfractured model, so the crack cannot be geometry. It is
// drawn as a line that WALKS THE SURFACE: step forward along the current tangent,
// raycast back onto the mesh to find where the surface actually is, re-project,
// repeat. That keeps it glued to the stone over curvature and around edges
// instead of floating across in a straight chord.
//
// Two things sell it beyond the line itself:
//   * it BRANCHES. Real fractures fork at flaws; a single unbranched line reads
//     as a drawn stroke.
//   * it advances in irregular bursts, not at constant speed. Stone fails by
//     jumping and arresting, and the ear expects each jump to have a sound.
public class TrailerStatueCrack : MonoBehaviour
{
    [Header("Shape")]
    public float stepLength = 0.18f;
    public float maxLength = 4.5f;
    [Tooltip("How far the crack may wander per step, in degrees. Low = a clean split, high = a shatter.")]
    public float wander = 34f;
    [Range(0f, 1f)] public float branchChance = 0.13f;
    public int maxBranches = 3;
    public int generation = 0;
    [Tooltip("Layers the surface walk may hit. Set by TrailerStatueShot to the statue's own layers — querying the whole scene costs a full raycast through terrain and trees for an answer that is discarded unless it landed on the statue.")]
    public int surfaceMask = ~0;

    [Header("Look")]
    public float widthAtMouth = 0.075f;
    public float widthAtTip = 0.012f;
    [Tooltip("The fissure itself — nearly black, so the crack reads as a hole before it reads as a light.")]
    public Color darkColor = new Color(0.02f, 0.01f, 0.04f, 1f);
    [Tooltip("What glows out of it once pressure builds behind.")]
    public Color glowColor = new Color(0.62f, 0.22f, 1f, 1f);

    private LineRenderer line;
    private Transform statue;
    private readonly List<Vector3> localPoints = new List<Vector3>(64);
    private Vector3 tip;        // world
    private Vector3 normal;     // world
    private Vector3 tangent;    // world, along the surface
    private float grown;
    private int branchesSpawned;
    // LineRenderer width is multiplied by the transform's scale, and the statue is
    // instantiated scaled up. Without this the crack widths below would mean
    // whatever the statue's scale happened to be instead of metres.
    private float scaleComp = 1f;
    private System.Action<Vector3, Vector3> onStep;   // pos, normal — for chips + sound

    public bool Finished => grown >= maxLength;
    public Vector3 Tip => tip;
    public Vector3 Mouth { get; private set; }

    public void Init(Transform statueRoot, Vector3 startPos, Vector3 startNormal, Material mat,
                     System.Action<Vector3, Vector3> stepCallback)
    {
        statue = statueRoot;
        tip = startPos;
        Mouth = startPos;
        normal = startNormal.normalized;
        onStep = stepCallback;

        // Any direction perpendicular to the surface will do to start; the walk
        // randomises it from there.
        tangent = Vector3.Cross(normal, Random.onUnitSphere).normalized;
        if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(normal, Vector3.up).normalized;

        line = gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;          // parented to the statue so it shakes WITH the stone
        line.material = mat;
        line.numCapVertices = 2;
        line.numCornerVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.alignment = LineAlignment.View;
        line.positionCount = 0;
        scaleComp = 1f / Mathf.Max(0.0001f, statue.lossyScale.x);

        // Points are stored in the STATUE's local space, and a non-world-space
        // LineRenderer resolves them against its OWN transform — so this object's
        // frame has to coincide with the statue's exactly, or every crack draws
        // at an offset. Parent first, then zero the local transform.
        transform.SetParent(statue, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        AddPoint(startPos);
    }

    private void AddPoint(Vector3 world)
    {
        localPoints.Add(statue.InverseTransformPoint(world));
        line.positionCount = localPoints.Count;
        for (int i = 0; i < localPoints.Count; i++) line.SetPosition(i, localPoints[i]);
    }

    // Advance the fracture. Returns true if it actually moved this call.
    public bool Advance()
    {
        if (Finished) return false;

        // Wander, then re-orthogonalise against the surface so the step stays
        // tangential rather than drifting off the stone.
        Quaternion twist = Quaternion.AngleAxis(Random.Range(-wander, wander), normal);
        tangent = twist * tangent;
        tangent = Vector3.ProjectOnPlane(tangent, normal).normalized;

        Vector3 candidate = tip + tangent * stepLength;

        // Re-project onto the surface: come in from slightly outside along the
        // old normal. Without this the line leaves the stone the moment the
        // surface curves.
        Vector3 from = candidate + normal * 0.35f;
        if (Physics.Raycast(from, -normal, out RaycastHit hit, 0.9f, surfaceMask, QueryTriggerInteraction.Ignore)
            && hit.transform.IsChildOf(statue))
        {
            tip = hit.point + hit.normal * 0.015f;
            normal = hit.normal;
        }
        else
        {
            // Walked off an edge. Try to catch the far side by aiming back at the
            // statue's axis; if that fails too, the crack has reached a boundary
            // and stops there, which is what real fractures do at an edge.
            Vector3 axis = new Vector3(statue.position.x, candidate.y, statue.position.z);
            Vector3 inward = (axis - candidate).normalized;
            if (Physics.Raycast(candidate - inward * 0.6f, inward, out RaycastHit edge, 1.4f, surfaceMask, QueryTriggerInteraction.Ignore)
                && edge.transform.IsChildOf(statue))
            {
                tip = edge.point + edge.normal * 0.015f;
                normal = edge.normal;
            }
            else
            {
                grown = maxLength;   // arrest
                return false;
            }
        }

        AddPoint(tip);
        grown += stepLength;
        onStep?.Invoke(tip, normal);
        return true;
    }

    public bool ShouldBranch()
    {
        if (generation >= 2 || branchesSpawned >= maxBranches) return false;
        if (localPoints.Count < 3) return false;
        if (Random.value > branchChance) return false;
        branchesSpawned++;
        return true;
    }

    public void GetBranchSeed(out Vector3 pos, out Vector3 nrm) { pos = tip; nrm = normal; }

    // `heat` 0..1 — how much pressure is behind the crack. The fissure darkens
    // the stone first and only then starts to glow, because a crack that lights
    // up the instant it appears looks like a decal switching on.
    public void SetHeat(float heat, float widthScale)
    {
        if (line == null) return;

        Color c = Color.Lerp(darkColor, glowColor, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((heat - 0.25f) / 0.75f)));
        // The mouth is always hotter than the tip: that is where the gap is
        // widest and the light behind is closest.
        line.startColor = c;
        line.endColor = Color.Lerp(darkColor, c, 0.35f);

        line.startWidth = widthAtMouth * widthScale * scaleComp;
        line.endWidth = widthAtTip * widthScale * scaleComp;
    }
}
