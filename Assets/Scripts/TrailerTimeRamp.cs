using UnityEngine;
using UnityEngine.Playables;

// Timeline-synced slow-motion for the lore trailer. Put it on the rig (next to
// the PlayableDirector). It reads the director's playhead (NOT wall-clock), so
// each pulse fires exactly when the Timeline reaches that moment and the slow-mo
// stays locked to the edit even as time dilates.
//
// Each pulse dips Time.timeScale down to 'minScale' with a smooth ease in / hold
// / ease out — the trailer punch you feel on a hoof-strike or a raised blade.
public class TrailerTimeRamp : MonoBehaviour
{
    [System.Serializable]
    public struct Pulse
    {
        [Tooltip("Timeline position (seconds) at the centre-start of the dip.")]
        public float atTime;
        [Tooltip("Ease-in seconds (timeline time).")]
        public float rampIn;
        [Tooltip("Seconds held at minScale.")]
        public float hold;
        [Tooltip("Ease-out seconds.")]
        public float rampOut;
        [Range(0.05f, 1f)] public float minScale;
    }

    [Tooltip("The rig's PlayableDirector (auto-filled from this object).")]
    public PlayableDirector director;
    public Pulse[] pulses;

    private void Reset() { director = GetComponent<PlayableDirector>(); }
    private void OnEnable() { if (director == null) director = GetComponent<PlayableDirector>(); }

    private void OnDisable()
    {
        // Always restore normal time when the shot/rig ends.
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
    }

    private void Update()
    {
        if (director == null) return;
        float t = (float)director.time;

        float scale = 1f;
        if (pulses != null)
        {
            foreach (var p in pulses)
            {
                float inEnd = p.atTime + Mathf.Max(0f, p.rampIn);
                float holdEnd = inEnd + Mathf.Max(0f, p.hold);
                float outEnd = holdEnd + Mathf.Max(0.0001f, p.rampOut);
                if (t < p.atTime || t > outEnd) continue;

                float v;
                if (t < inEnd) v = Mathf.InverseLerp(p.atTime, inEnd, t);
                else if (t < holdEnd) v = 1f;
                else v = 1f - Mathf.InverseLerp(holdEnd, outEnd, t);
                v = v * v * (3f - 2f * v);                       // smoothstep
                scale = Mathf.Min(scale, Mathf.Lerp(1f, p.minScale, v));
            }
        }

        Time.timeScale = scale;
        Time.fixedDeltaTime = 0.02f * Mathf.Max(0.05f, scale);
    }
}
