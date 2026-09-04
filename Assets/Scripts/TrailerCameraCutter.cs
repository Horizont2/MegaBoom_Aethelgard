using UnityEngine;
using Unity.Cinemachine;

// Cuts between a set of Cinemachine cameras on a timeline of cut times (its own
// scaled clock, matching the horse ride). It just raises the chosen camera's
// Priority so the CinemachineBrain blends to it — no PlayableDirector needed.
// Used for the Act II season journey so several follow angles cover the ride.
public class TrailerCameraCutter : MonoBehaviour
{
    public CinemachineCamera[] cameras;

    [Header("Cut by ROUTE progress (recommended — works for any route length)")]
    public bool useProgress = true;
    public TrailerHorseRide ride;
    [Tooltip("0..1 progress at which to cut to cameras[i]. Same length as cameras.")]
    public float[] cutProgress;

    [Header("Or cut by time")]
    [Tooltip("Seconds (from start) at which to cut to cameras[i]. Used when useProgress is off.")]
    public float[] cutTimes;

    private float _clock;
    private int _idx = -1;

    private void OnEnable() { _clock = 0f; _idx = -1; Cut(0); }

    private void Update()
    {
        int want = 0;
        if (useProgress && ride != null && cutProgress != null)
        {
            float p = Mathf.Clamp01(ride.progress01);
            int n = Mathf.Min(cutProgress.Length, cameras != null ? cameras.Length : 0);
            for (int i = 0; i < n; i++) if (p >= cutProgress[i]) want = i;
        }
        else
        {
            _clock += Time.deltaTime;
            int n = Mathf.Min(cutTimes != null ? cutTimes.Length : 0, cameras != null ? cameras.Length : 0);
            for (int i = 0; i < n; i++) if (_clock >= cutTimes[i]) want = i;
        }
        if (want != _idx) Cut(want);
    }

    [Tooltip("Snap the camera we cut to straight to its framing (no damped glide in from where it last was). This is what makes a cut read as a CUT.")]
    public bool hardCuts = true;

    private void Cut(int i)
    {
        if (cameras == null) return;
        _idx = i;
        for (int c = 0; c < cameras.Length; c++)
        {
            if (cameras[c] == null) continue;
            var p = cameras[c].Priority;
            p.Value = (c == i) ? 100 : 0;
            cameras[c].Priority = p;
        }

        if (hardCuts && i >= 0 && i < cameras.Length && cameras[i] != null)
        {
            // deltaTime < 0 tells Cinemachine to evaluate with damping disabled,
            // so the shot is already correctly framed on its first frame.
            cameras[i].PreviousStateIsValid = false;
            cameras[i].InternalUpdateCameraState(Vector3.up, -1f);
        }
    }
}
