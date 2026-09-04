using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

// Rides the horse along a Spline for the lore-trailer road shot, so its path is
// authored, repeatable and camera-trackable. Put it on the horse root (it also
// carries HorseAudioController, which plays hooves/breathing off the movement).
//
// Two common setup snags this handles WITHOUT redrawing the spline:
//   * horse runs from the wrong END  → tick 'Reverse'.
//   * horse runs BACKWARDS / spine-first → set 'Model Yaw Offset' to 180
//     (its model's forward faces the other way).
//
// Timing: leave 'Drive From Timeline' OFF and set 'Auto Fit Seconds' to how
// long the ride should take (it computes speed to cover the whole path in that
// time). Start it together with the camera Timeline and they stay in sync — no
// Animation-track keys on the horse needed (those were causing the mid-timeline
// teleport). If you really want frame-locked control, tick 'Drive From
// Timeline' and animate 'Progress 01' on a single continuous 0→1 clip.
public class TrailerHorseRide : MonoBehaviour
{
    [Tooltip("Spline drawn along the road for the horse to gallop down.")]
    public SplineContainer path;

    [Header("Direction / facing (fix without redrawing the spline)")]
    [Tooltip("Travel from the OTHER end of the spline (1→0).")]
    public bool reverse = false;
    [Tooltip("Extra yaw (°) added to the facing. Set 180 if the horse runs backwards / spine-first.")]
    public float modelYawOffset = 0f;
    [Tooltip("Turn the horse to face along the path.")]
    public bool faceAlongPath = true;

    [Header("Timing")]
    [Tooltip("Ride the whole path over this many seconds (0 = use Speed instead). Set it to the length of Act I so it lines up with the cameras.")]
    public float autoFitSeconds = 18f;
    [Tooltip("Metres/sec (used only when Auto Fit Seconds is 0).")]
    public float speed = 12f;
    public bool playOnStart = true;
    public bool loop = false;
    [Tooltip("When galloping past the end of the spline, keep the horse ON the ground (raycast to terrain) so it doesn't ride straight through hills/textures during the final crane.")]
    public bool groundSnapOverrun = true;

    [Header("Pace")]
    [Tooltip("Speed multiplier across the route (X = progress 0..1, Y = multiplier). A flat curve is the old constant pace. Rising toward the end makes a chase ACCELERATE, which is what a chase does — a constant gallop reads as travel, not flight. Auto Fit Seconds still sets the average, so the run takes the same time overall.")]
    public AnimationCurve paceCurve = AnimationCurve.Constant(0f, 1f, 1f);

    [Header("Timeline (optional, advanced)")]
    [Tooltip("Don't auto-advance — a Timeline animates Progress 01 instead.")]
    public bool driveFromTimeline = false;
    [Range(0f, 1f)] public float progress01 = 0f;

    [Tooltip("Animator trigger for the gallop (the evacuation horse uses 'Run').")]
    public string runTrigger = "Run";

    private Animator _anim;
    private bool _started;
    // Metres travelled straight ahead PAST the end of the spline. Instead of
    // freezing at the last knot and galloping in place, the horse keeps riding
    // off into the distance — which reads naturally under the final crane shot.
    private float _overrun;

    private void OnEnable()
    {
        _anim = GetComponentInChildren<Animator>();
        if (playOnStart) BeginRide();
        else ApplyProgress(progress01);
    }

    [ContextMenu("Begin Ride")]
    public void BeginRide()
    {
        _started = true;
        _overrun = 0f;
        if (_anim != null)
        {
            // Root motion would fight our spline-driven transform (in-place
            // gallop is what we want), so switch it off.
            _anim.applyRootMotion = false;
            if (!string.IsNullOrEmpty(runTrigger)) _anim.SetTrigger(runTrigger);
        }
        ApplyProgress(progress01);
    }

    // Discard the gallop clip's root motion entirely. The horse clips are
    // authored WITH root motion (Rig_Gallop_*_RootMotion); if that motion is
    // applied it fights our spline transform and the horse stutters backwards.
    // An empty OnAnimatorMove consumes the root-motion delta so it never touches
    // the transform (belt-and-suspenders with applyRootMotion=false + the clips
    // now being baked in-place).
    private void OnAnimatorMove() { }

    // Drive the transform in LateUpdate — AFTER the Animator has evaluated for
    // the frame — so the spline position is always the final word and the
    // gallop can never nudge the horse off the path.
    private void LateUpdate()
    {
        if (path == null) return;

        if (!driveFromTimeline)
        {
            if (!_started) return;
            float len = path.CalculateLength();
            if (len < 0.01f) return;
            float mps = autoFitSeconds > 0.01f ? (len / autoFitSeconds) : speed;
            if (paceCurve != null && paceCurve.length > 0)
                mps *= Mathf.Max(0.05f, paceCurve.Evaluate(progress01));

            if (progress01 < 1f)
            {
                progress01 += (mps / len) * Time.deltaTime;
                if (progress01 >= 1f) progress01 = loop ? progress01 - 1f : 1f;
            }
            else if (!loop)
            {
                // Reached the end of the path: keep galloping straight ahead so
                // the horse rides off into the valley instead of running in place.
                _overrun += mps * Time.deltaTime;
            }
        }

        ApplyProgress(progress01);
    }

    private void ApplyProgress(float t)
    {
        if (path == null) return;
        t = Mathf.Clamp01(t);
        float eval = reverse ? 1f - t : t;

        float3 p = path.EvaluatePosition(eval);
        float3 tan = path.EvaluateTangent(eval);
        Vector3 dir = new Vector3(tan.x, 0f, tan.z);
        if (reverse) dir = -dir;                           // tangent points along +t; flip when reversed

        Vector3 pos = new Vector3(p.x, p.y, p.z);
        if (_overrun > 0f && dir.sqrMagnitude > 0.0001f)
        {
            pos += dir.normalized * _overrun;              // keep riding past the last knot
            // Follow the terrain past the spline so the horse rides OVER the
            // ground, not straight through hills/textures during the reveal.
            if (groundSnapOverrun && TryGroundY(pos, out float gy)) pos.y = gy;
        }
        transform.position = pos;

        if (faceAlongPath && dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir.normalized) * Quaternion.Euler(0f, modelYawOffset, 0f);
    }

    private static readonly string[] GroundNames = { "terrain", "floor", "ground", "road", "path", "plane" };

    // Height of the real ground under 'pos' — accepts only terrain / ground-named
    // colliders (never the horse itself, trees, etc.), so the overrun rides the
    // surface instead of clipping through it.
    private bool TryGroundY(Vector3 pos, out float y)
    {
        y = pos.y;
        RaycastHit[] hits = Physics.RaycastAll(pos + Vector3.up * 12f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        bool found = false;
        foreach (var h in hits)
        {
            var col = h.collider;
            if (col == null) continue;
            if (col.transform.IsChildOf(transform)) continue;          // not the horse/rider
            bool isGround = col.GetComponentInParent<Terrain>() != null;
            if (!isGround)
            {
                string n = col.name.ToLowerInvariant();
                foreach (var g in GroundNames) if (n.Contains(g)) { isGround = true; break; }
            }
            if (!isGround) continue;
            if (h.point.y > best) { best = h.point.y; found = true; }
        }
        if (found) { y = best; return true; }
        return false;
    }
}
