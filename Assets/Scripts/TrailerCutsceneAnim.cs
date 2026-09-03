using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Plays a specific AnimationClip directly on an Animator via a PlayableGraph,
// OVER whatever controller it already runs — no states/triggers/parameters
// needed (that's why the controller-based look-back/fall wouldn't fire). The
// base pose keeps coming from the controller (input 0); the clip is overlaid on
// input 1, optionally through an upper-body AvatarMask so the legs keep riding.
public class TrailerCutsceneAnim : MonoBehaviour
{
    public Animator animator;

    private PlayableGraph _graph;
    private AnimationLayerMixerPlayable _mixer;
    private AnimationClipPlayable _overlay;
    private bool _built, _hold, _active;
    private float _len, _t;

    private void Awake() { if (animator == null) animator = GetComponentInChildren<Animator>(); }
    private void OnDisable() { if (_graph.IsValid()) _graph.Destroy(); _built = false; _active = false; }

    private void Build()
    {
        if (_built) return;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null) return;
        _graph = PlayableGraph.Create("TrailerAnim_" + animator.name);
        var output = AnimationPlayableOutput.Create(_graph, "out", animator);
        _mixer = AnimationLayerMixerPlayable.Create(_graph, 2);
        var ctrl = AnimatorControllerPlayable.Create(_graph, animator.runtimeAnimatorController);
        _graph.Connect(ctrl, 0, _mixer, 0);
        _mixer.SetInputWeight(0, 1f);
        output.SetSourcePlayable(_mixer);
        _graph.Play();
        _built = true;
    }

    // mask null = full-body override; hold = freeze on the last frame (fall/rear),
    // otherwise it fades back to the controller after the clip (look-back).
    public void Play(AnimationClip clip, AvatarMask mask, bool hold)
    {
        if (clip == null) return;
        Build();
        if (!_built) return;
        if (_overlay.IsValid()) { _graph.Disconnect(_mixer, 1); _overlay.Destroy(); }
        _overlay = AnimationClipPlayable.Create(_graph, clip);
        _overlay.SetApplyFootIK(false);
        _graph.Connect(_overlay, 0, _mixer, 1);
        _mixer.SetInputWeight(1, 1f);
        if (mask != null) _mixer.SetLayerMaskFromAvatarMask(1, mask);
        _hold = hold; _len = Mathf.Max(0.05f, clip.length); _t = 0f; _active = true;
    }

    private void Update()
    {
        if (!_active || !_built || !_overlay.IsValid()) return;
        _t += Time.deltaTime;
        if (_t < _len) return;

        if (_hold)
        {
            _overlay.SetSpeed(0);
            _overlay.SetTime(_len);
        }
        else
        {
            float w = Mathf.Clamp01(1f - (_t - _len) / 0.3f);
            _mixer.SetInputWeight(1, w);
            if (w <= 0f) { _graph.Disconnect(_mixer, 1); _overlay.Destroy(); _active = false; }
        }
    }
}
