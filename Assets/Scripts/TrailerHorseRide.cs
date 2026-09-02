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

    [Header("Timeline (optional, advanced)")]
    [Tooltip("Don't auto-advance — a Timeline animates Progress 01 instead.")]
    public bool driveFromTimeline = false;
    [Range(0f, 1f)] public float progress01 = 0f;

    [Tooltip("Animator trigger for the gallop (the evacuation horse uses 'Run').")]
    public string runTrigger = "Run";

    private Animator _anim;
    private bool _started;

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
        if (_anim != null && !string.IsNullOrEmpty(runTrigger)) _anim.SetTrigger(runTrigger);
        ApplyProgress(progress01);
    }

    private void Update()
    {
        if (path == null) return;

        if (!driveFromTimeline)
        {
            if (!_started) return;
            float len = path.CalculateLength();
            if (len < 0.01f) return;
            float mps = autoFitSeconds > 0.01f ? (len / autoFitSeconds) : speed;
            progress01 += (mps / len) * Time.deltaTime;
            if (progress01 >= 1f) progress01 = loop ? progress01 - 1f : 1f;
        }

        ApplyProgress(progress01);
    }

    private void ApplyProgress(float t)
    {
        if (path == null) return;
        t = Mathf.Clamp01(t);
        float eval = reverse ? 1f - t : t;

        float3 p = path.EvaluatePosition(eval);
        transform.position = new Vector3(p.x, p.y, p.z);

        if (faceAlongPath)
        {
            float3 tan = path.EvaluateTangent(eval);
            Vector3 dir = new Vector3(tan.x, 0f, tan.z);
            if (reverse) dir = -dir;                       // tangent points along +t; flip when reversed
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir.normalized) * Quaternion.Euler(0f, modelYawOffset, 0f);
        }
    }
}
