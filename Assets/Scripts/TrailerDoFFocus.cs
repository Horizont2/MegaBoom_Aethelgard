using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Playables;

// Cinematic depth-of-field that keeps the HORSE sharp while softening the
// background — but ONLY during the close shots, and never on the wide reveal, so
// the valley stays fully visible. It auto-focuses on the horse each frame
// (focus distance = camera→horse), so the subject is always crisp.
//
// DoF is a global Volume override; this drives the DepthOfField on the
// Trailer_PostFX volume, enabling it only inside the given Timeline windows.
public class TrailerDoFFocus : MonoBehaviour
{
    public PlayableDirector director;
    public Transform focusTarget;      // the horse
    public Camera cam;                 // Main Camera
    [Tooltip("Timeline windows (x=start, y=end, seconds) where DoF is ON — the close shots only.")]
    public Vector2[] activeRanges;
    public float minFocus = 1.5f;

    private DepthOfField _dof;

    private void OnEnable()
    {
        if (cam == null) cam = Camera.main;
        _dof = FindDoF();
        if (_dof != null) _dof.active = false;
    }

    private void OnDisable() { if (_dof != null) _dof.active = false; }

    private void Update()
    {
        if (_dof == null) { _dof = FindDoF(); if (_dof == null) return; }
        if (cam == null) { cam = Camera.main; if (cam == null) return; }

        float t = director != null ? (float)director.time : 0f;
        bool on = false;
        if (activeRanges != null)
            foreach (var r in activeRanges)
                if (t >= r.x && t <= r.y) { on = true; break; }

        _dof.active = on;
        if (on && focusTarget != null)
        {
            float d = Vector3.Distance(cam.transform.position, focusTarget.position);
            _dof.focusDistance.overrideState = true;
            _dof.focusDistance.value = Mathf.Max(minFocus, d);
        }
    }

    private DepthOfField FindDoF()
    {
        var go = GameObject.Find("Trailer_PostFX");
        if (go != null)
        {
            var v = go.GetComponent<Volume>();
            if (v != null && v.sharedProfile != null && v.sharedProfile.TryGet(out DepthOfField d)) return d;
        }
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            if (v.profile != null && v.profile.TryGet(out DepthOfField d2)) return d2;
        return null;
    }
}
