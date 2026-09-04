using UnityEngine;

// Wading / swimming state. Once the player is submerged past the waist they
// switch to the airborne (arms-out) pose, cannot attack, and bob with the
// surface. Movement itself is untouched — they still walk the bottom — so this
// adds no physics of its own and can't fight the CharacterController.
//
// Put it on the player root, next to PlayerController.
[RequireComponent(typeof(CharacterController))]
public class PlayerWaterState : MonoBehaviour
{
    [Tooltip("Fraction of body height that must be under water before the swim state kicks in. 0.5 = past the waist.")]
    [Range(0.1f, 1f)] public float submergeThreshold = 0.5f;

    [Tooltip("Fraction it must drop back below to leave the state. Kept lower than the entry threshold so standing exactly at waist depth doesn't flicker in and out.")]
    [Range(0.05f, 1f)] public float surfaceThreshold = 0.42f;

    [Header("Bob")]
    [Tooltip("How far the body rides up and down with the water, in metres.")]
    public float bobAmplitude = 0.09f;
    public float bobSpeed = 1.6f;
    [Tooltip("Gentle roll, in degrees.")]
    public float bobRoll = 2.2f;
    [Tooltip("The visual model to bob. Left empty, the Animator's transform is used — never the root, or bobbing would fight the CharacterController.")]
    public Transform visualRoot;

    [Header("Read-only")]
    public bool isSubmerged;
    [Tooltip("0 = feet at the surface, 1 = fully under.")]
    public float submersion;

    private CharacterController _cc;
    private PlayerController _player;
    private Animator _anim;
    private Vector3 _visualBaseLocalPos;
    private Quaternion _visualBaseLocalRot;
    private bool _visualCached;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _player = GetComponent<PlayerController>();
        _anim = GetComponentInChildren<Animator>();
        if (visualRoot == null && _anim != null) visualRoot = _anim.transform;
        if (visualRoot != null && visualRoot != transform)
        {
            _visualBaseLocalPos = visualRoot.localPosition;
            _visualBaseLocalRot = visualRoot.localRotation;
            _visualCached = true;
        }
    }

    private void OnDisable() { ResetVisual(); if (_player != null) _player.isSwimming = false; }

    private void Update()
    {
        float bodyHeight = Mathf.Max(0.5f, _cc.height * transform.lossyScale.y);
        float feetY = transform.position.y + (_cc.center.y * transform.lossyScale.y) - bodyHeight * 0.5f;

        submersion = 0f;
        if (WaterBody.TrySurfaceAt(transform.position, out float surfaceY))
            submersion = Mathf.Clamp01((surfaceY - feetY) / bodyHeight);

        // Separate enter and exit thresholds, so wading at exactly waist depth
        // doesn't strobe between the two states.
        bool want = isSubmerged ? submersion > surfaceThreshold : submersion >= submergeThreshold;
        isSubmerged = want;

        if (_player != null) _player.isSwimming = isSubmerged;

        if (isSubmerged) ApplyBob();
        else ResetVisual();
    }

    private void ApplyBob()
    {
        if (!_visualCached) return;
        float t = Time.time * bobSpeed;
        // Two frequencies so it reads as water rather than a metronome.
        float y = (Mathf.Sin(t) * 0.7f + Mathf.Sin(t * 1.7f + 1.1f) * 0.3f) * bobAmplitude;
        float roll = Mathf.Sin(t * 0.8f + 0.4f) * bobRoll;

        visualRoot.localPosition = _visualBaseLocalPos + new Vector3(0f, y, 0f);
        visualRoot.localRotation = _visualBaseLocalRot * Quaternion.Euler(0f, 0f, roll);
    }

    private void ResetVisual()
    {
        if (!_visualCached || visualRoot == null) return;
        visualRoot.localPosition = Vector3.Lerp(visualRoot.localPosition, _visualBaseLocalPos, 1f - Mathf.Exp(-10f * Time.deltaTime));
        visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation, _visualBaseLocalRot, 1f - Mathf.Exp(-10f * Time.deltaTime));
    }
}
