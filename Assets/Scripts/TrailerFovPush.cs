using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.Playables;

// A slow, subtle push-in (FOV creep) for the opening static shot, so it breathes
// instead of sitting dead still. Timeline-synced via the director playhead.
[RequireComponent(typeof(CinemachineCamera))]
public class TrailerFovPush : MonoBehaviour
{
    public PlayableDirector director;
    public float startTime = 0f;
    public float duration = 5f;
    public float startFov = 42f;
    public float endFov = 36f;

    private CinemachineCamera _cam;

    private void OnEnable()
    {
        _cam = GetComponent<CinemachineCamera>();
        SetFov(startFov);
    }

    private void Update()
    {
        if (_cam == null) return;
        float t = director != null ? (float)director.time : 0f;
        float local = t - startTime;
        float k = duration > 0.01f ? Mathf.Clamp01(local / duration) : 1f;
        k = k * k * (3f - 2f * k);   // smoothstep
        SetFov(Mathf.Lerp(startFov, endFov, k));
    }

    private void SetFov(float fov)
    {
        var lens = _cam.Lens;
        lens.FieldOfView = fov;
        _cam.Lens = lens;
    }
}
