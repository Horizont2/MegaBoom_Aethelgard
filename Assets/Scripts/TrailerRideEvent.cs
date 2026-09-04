using System.Collections;
using UnityEngine;

// Part-2 climax beats along the ride:
//   lookBackProgress — the rider glances back over the shoulder.
//   strikeProgress   — lightning cracks down beside the horse: white flash and
//                      thunder, the horse REARS and holds, the rider is thrown,
//                      FALLS physically to the ground, lands, and gets up.
//
// The rider's controller (OnHorseAnimator) already carries LookBack / Fall /
// GetUp triggers, so those are fired directly — that is the reliable path now
// that the rig is set up. TrailerCutsceneAnim is only the fallback for an
// animator whose controller has no such parameter.
public class TrailerRideEvent : MonoBehaviour
{
    public TrailerHorseRide ride;
    [Range(0f, 1f)] public float lookBackProgress = 0.35f;
    [Range(0f, 1f)] public float strikeProgress = 0.88f;

    [Header("Animator triggers (used when the controller has them)")]
    public string lookBackTrigger = "LookBack";
    public string fallTrigger = "Fall";
    public string getUpTrigger = "GetUp";
    public string horseRearTrigger = "Rear";

    [Header("Clip fallback (only if the controller has no such trigger)")]
    public AnimationClip lookBehindClip;
    public AnimationClip fallingBackClip;
    public AnimationClip horseRearClip;
    public AvatarMask upperBodyMask;
    [Range(0f, 1f)] public float lookBackWeight = 0.85f;
    public AvatarMask fallMask;
    public AvatarMask horseMask;

    [Header("Fall physics")]
    [Tooltip("Sideways shove off the saddle.")]
    public float throwSideways = 2.0f;
    [Tooltip("Backwards shove off the saddle.")]
    public float throwBackwards = 1.4f;
    [Tooltip("Upward kick before gravity takes over.")]
    public float throwUp = 2.6f;
    public float fallGravity = 14f;
    [Tooltip("Seconds on the ground before he pushes himself up.")]
    public float getUpDelay = 1.4f;

    [Header("Fall camera (cuts in at the strike)")]
    public bool useFallCamera = true;
    public float fallCamDistance = 7.5f;
    public float fallCamSide = 4.0f;
    public float fallCamHeight = 1.3f;
    public float fallCamFov = 42f;

    [Header("Strike direction")]
    [Tooltip("How far to the side of the horse the bolt lands.")]
    public float strikeSideOffset = 4.5f;
    [Tooltip("Metres AHEAD of the horse — the bolt cuts him off, which is what makes the horse rear.")]
    public float strikeForwardOffset = 6f;
    [Tooltip("Beat between the flash and the horse rearing, so cause reads before effect.")]
    public float rearDelay = 0.18f;
    [Tooltip("Beat between the rear and the rider losing his seat.")]
    public float throwDelay = 0.45f;

    [Header("Sound")]
    public string neighId = "Animals/Horse_Snort";

    private TrailerLightningStrike _bolt;
    private Transform _riderGO;
    private Animator _riderAnimator, _horseAnimator;
    private TrailerCutsceneAnim _riderAnim, _horseAnim;
    private bool _struck, _lookedBack;

    private void OnEnable()
    {
        _struck = false; _lookedBack = false;

        if (ride != null)
        {
            _horseAnimator = ride.GetComponent<Animator>();
            foreach (var a in ride.GetComponentsInChildren<Animator>(true))
            {
                if (a == null) continue;
                if (a == _horseAnimator) continue;
                if (a.transform == ride.transform) { _horseAnimator ??= a; continue; }
                if (_riderAnimator == null) { _riderAnimator = a; _riderGO = TopUnder(a.transform, ride.transform); }
            }
            _horseAnimator ??= ride.GetComponentInChildren<Animator>(true);

            _horseAnim = GetOrAdd(ride.gameObject, _horseAnimator);
            if (_riderGO != null) _riderAnim = GetOrAdd(_riderGO.gameObject, _riderAnimator);
        }

        Debug.Log($"[Trailer] RideEvent ready — rider='{(_riderGO ? _riderGO.name : "NOT FOUND")}' " +
                  $"riderCtrl='{(_riderAnimator != null && _riderAnimator.runtimeAnimatorController != null ? _riderAnimator.runtimeAnimatorController.name : "none")}' " +
                  $"triggers: look={HasParam(_riderAnimator, lookBackTrigger)} fall={HasParam(_riderAnimator, fallTrigger)} getUp={HasParam(_riderAnimator, getUpTrigger)}");

        if (_bolt == null)
        {
            var go = new GameObject("Trailer_LightningBolt");
            go.transform.SetParent(transform, false);
            go.AddComponent<LineRenderer>();
            _bolt = go.AddComponent<TrailerLightningStrike>();
        }
    }

    private void Update()
    {
        if (ride == null) return;

        if (!_lookedBack && ride.progress01 >= lookBackProgress)
        {
            _lookedBack = true;
            if (!Fire(_riderAnimator, lookBackTrigger) && _riderAnim != null)
                _riderAnim.Play(lookBehindClip, upperBodyMask, hold: false, weight: lookBackWeight);
        }

        if (!_struck && ride.progress01 >= strikeProgress)
        {
            _struck = true;
            StartCoroutine(StrikeRoutine());
        }
    }

    // Flash → horse rears → rider is thrown → he falls to the ground → gets up.
    // Staged so each beat reads, instead of everything happening on one frame.
    private IEnumerator StrikeRoutine()
    {
        var t = ride.transform;

        // The bolt lands AHEAD and to the side: it cuts the horse off, which is
        // the reason he rears. Landing it beside/behind him read as unrelated.
        Vector3 gp = t.position + t.forward * strikeForwardOffset + t.right * strikeSideOffset;
        if (TryGround(gp, out float gy)) gp.y = gy;
        if (_bolt != null) _bolt.Strike(gp);
        CameraShakeUtil.TryShake(0.45f, 0.25f);

        CutToFallCamera();

        yield return new WaitForSeconds(rearDelay);

        // Horse rears and holds.
        ride.enabled = false;
        if (AudioManager.Instance != null && !string.IsNullOrEmpty(neighId)) AudioManager.Instance.PlaySFX(neighId);
        if (!Fire(_horseAnimator, horseRearTrigger) && _horseAnim != null)
            _horseAnim.Play(horseRearClip, horseMask, hold: true);

        yield return new WaitForSeconds(throwDelay);

        if (_riderGO == null) yield break;

        // Off the saddle, then a REAL fall to the ground — previously the fall
        // animation played in mid-air because nothing ever brought him down.
        _riderGO.SetParent(null, true);
        if (!Fire(_riderAnimator, fallTrigger) && _riderAnim != null)
            _riderAnim.Play(fallingBackClip, fallMask, hold: true);

        Vector3 vel = (-t.forward * throwBackwards) + (t.right * throwSideways) + (Vector3.up * throwUp);
        float groundY = TryGround(_riderGO.position, out float g0) ? g0 : _riderGO.position.y - 2f;

        while (_riderGO.position.y > groundY + 0.02f)
        {
            vel.y -= fallGravity * Time.deltaTime;
            _riderGO.position += vel * Time.deltaTime;
            if (TryGround(_riderGO.position, out float g)) groundY = g;
            if (_riderGO.position.y <= groundY)
            {
                var p = _riderGO.position; p.y = groundY; _riderGO.position = p;
                break;
            }
            yield return null;
        }

        // Landed: face away from the horse, sprawled on the ground.
        Vector3 away = _riderGO.position - t.position; away.y = 0f;
        if (away.sqrMagnitude > 0.01f) _riderGO.rotation = Quaternion.LookRotation(away.normalized);
        CameraShakeUtil.TryShake(0.25f, 0.12f);

        yield return new WaitForSeconds(getUpDelay);
        if (!Fire(_riderAnimator, getUpTrigger) && _riderAnim != null && fallingBackClip != null)
            _riderAnim.Play(fallingBackClip, fallMask, hold: true);
    }

    // Cut to a low camera set off to the side so BOTH the rearing horse and the
    // falling rider are in frame — the old one sat too close to read.
    private void CutToFallCamera()
    {
        if (!useFallCamera) return;

        var t = ride.transform;
        Vector3 pos = t.position + t.forward * fallCamDistance - t.right * fallCamSide;
        if (TryGround(pos, out float gy)) pos.y = gy;
        pos.y += fallCamHeight;

        var go = new GameObject("CM_Part2_Fall");
        go.transform.SetParent(transform, false);
        go.transform.position = pos;

        Vector3 look = t.position + Vector3.up * 1.4f;
        go.transform.rotation = Quaternion.LookRotation((look - pos).normalized);

        var cam = go.AddComponent<Unity.Cinemachine.CinemachineCamera>();
        cam.Lens.FieldOfView = fallCamFov;
        var pr = cam.Priority; pr.Value = 300; cam.Priority = pr;   // beats the cutter's 100

        var brain = Object.FindFirstObjectByType<Unity.Cinemachine.CinemachineBrain>();
        if (brain != null)
            brain.DefaultBlend = new Unity.Cinemachine.CinemachineBlendDefinition(
                Unity.Cinemachine.CinemachineBlendDefinition.Styles.Cut, 0f);

        cam.PreviousStateIsValid = false;
        cam.InternalUpdateCameraState(Vector3.up, -1f);
    }

    // ── Animator helpers ─────────────────────────────────────────────────
    private static bool HasParam(Animator a, string param)
    {
        if (a == null || string.IsNullOrEmpty(param) || a.runtimeAnimatorController == null) return false;
        foreach (var p in a.parameters) if (p.name == param) return true;
        return false;
    }

    private static bool Fire(Animator a, string param)
    {
        if (!HasParam(a, param)) return false;
        a.ResetTrigger(param);
        a.SetTrigger(param);
        Debug.Log($"[Trailer] Fired '{param}' on '{a.name}'.");
        return true;
    }

    private static TrailerCutsceneAnim GetOrAdd(GameObject go, Animator anim)
    {
        var c = go.GetComponent<TrailerCutsceneAnim>() ?? go.AddComponent<TrailerCutsceneAnim>();
        if (anim != null) c.animator = anim;
        return c;
    }

    private static Transform TopUnder(Transform t, Transform root)
    {
        var cur = t;
        while (cur != null && cur.parent != null && cur.parent != root) cur = cur.parent;
        return cur;
    }

    private static readonly string[] GroundNames = { "terrain", "ground", "floor", "road", "path" };
    private static bool TryGround(Vector3 pos, out float y)
    {
        y = pos.y;
        var hits = Physics.RaycastAll(pos + Vector3.up * 20f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity; bool found = false;
        foreach (var h in hits)
        {
            var col = h.collider; if (col == null) continue;
            bool g = col.GetComponentInParent<Terrain>() != null;
            if (!g) { string n = col.name.ToLowerInvariant(); foreach (var s in GroundNames) if (n.Contains(s)) { g = true; break; } }
            if (!g) continue;
            if (h.point.y > best) { best = h.point.y; found = true; }
        }
        if (found) { y = best; return true; }
        return false;
    }
}
