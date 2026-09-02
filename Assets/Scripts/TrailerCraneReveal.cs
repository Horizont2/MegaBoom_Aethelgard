using UnityEngine;
using Unity.Cinemachine;

// Time-synced crane for the FINAL Act I shot ("rise over the valley").
//
// Once the Timeline reaches this camera's clip (startDelay seconds after the
// trailer begins), it lifts the CinemachineFollow offset from a low chase up to
// a high, wide vista over 'duration' seconds while the RotationComposer keeps
// the (now receding) horse framed — so instead of the horse freezing and
// galloping in place at the end, the camera cranes up and the rider gallops off
// into the valley.
//
// No Cinemachine "is-live" API is needed: the PlayableDirector plays on awake at
// t=0 and the horse starts riding at t=0, so a plain timer stays in sync with
// the Timeline. The Act I setup tool fills startDelay/duration from the actual
// CM_04 clip on the Timeline.
[RequireComponent(typeof(CinemachineFollow))]
public class TrailerCraneReveal : MonoBehaviour
{
    [Tooltip("Chase offset at the start of the shot (matches the follow cam).")]
    public Vector3 startOffset = new Vector3(0f, 6.5f, -9f);
    [Tooltip("High, wide vista offset at the end of the crane.")]
    public Vector3 endOffset = new Vector3(0f, 26f, -42f);
    [Tooltip("Seconds after the trailer starts when this shot begins (its Timeline clip start).")]
    public float startDelay = 15f;
    [Tooltip("How long the crane takes to reach the wide vista.")]
    public float duration = 5f;

    private CinemachineFollow _follow;
    private float _t;

    private void OnEnable()
    {
        _follow = GetComponent<CinemachineFollow>();
        _t = 0f;
        if (_follow != null) _follow.FollowOffset = startOffset;
    }

    private void Update()
    {
        if (_follow == null) return;
        _t += Time.deltaTime;

        float local = _t - startDelay;
        if (local <= 0f) { _follow.FollowOffset = startOffset; return; }

        float k = duration > 0.01f ? Mathf.Clamp01(local / duration) : 1f;
        k = k * k * (3f - 2f * k);   // smoothstep ease-in-out
        _follow.FollowOffset = Vector3.Lerp(startOffset, endOffset, k);
    }
}
