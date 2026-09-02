using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

// Rides the horse along a Spline for the lore-trailer road shot, so its path is
// authored and repeatable and the Cinemachine cameras can track it cleanly.
// Put it on the horse root (the object that also carries HorseAudioController —
// the audio driver detects the movement and plays hooves/breathing on its own).
//
//   * Draw a Spline along the road (GameObject ▸ Spline) and assign it to Path.
//   * Auto mode: it advances along the path at Speed on its own — good for a
//     quick preview.
//   * Timeline mode: tick Drive From Timeline and animate 'Progress 01' (0→1)
//     on an Animation track, so the ride is locked to the shot and scrubbable.
//
// The horse's own Y comes from the spline, so author the spline at road height
// (no terrain snapping — the path is exact).
public class TrailerHorseRide : MonoBehaviour
{
    [Tooltip("Spline drawn along the road for the horse to gallop down.")]
    public SplineContainer path;

    [Tooltip("Metres/sec along the path when auto-playing.")]
    public float speed = 12f;

    [Tooltip("Start riding on enable (quick preview). Off if the Timeline drives it.")]
    public bool playOnStart = true;

    public bool loop = false;

    [Tooltip("Don't auto-advance — the Timeline animates Progress 01 instead (locks the ride to the shot).")]
    public bool driveFromTimeline = false;

    [Range(0f, 1f)] public float progress01 = 0f;

    [Tooltip("Turn the horse to face straight down the path.")]
    public bool faceAlongPath = true;

    [Tooltip("Animator trigger for the gallop (the evacuation horse uses 'Run').")]
    public string runTrigger = "Run";

    private Animator _anim;
    private bool _started;

    private void OnEnable()
    {
        _anim = GetComponentInChildren<Animator>();
        if (playOnStart) BeginRide();
        else ApplyProgress(progress01);   // sit at the start pose
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
            progress01 += (speed / len) * Time.deltaTime;
            if (progress01 >= 1f) progress01 = loop ? progress01 - 1f : 1f;
        }

        ApplyProgress(progress01);
    }

    private void ApplyProgress(float t)
    {
        if (path == null) return;
        t = Mathf.Clamp01(t);

        float3 p = path.EvaluatePosition(t);
        transform.position = new Vector3(p.x, p.y, p.z);

        if (faceAlongPath)
        {
            float3 tan = path.EvaluateTangent(t);
            Vector3 dir = new Vector3(tan.x, 0f, tan.z);
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir.normalized);
        }
    }
}
