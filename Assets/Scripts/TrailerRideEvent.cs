using UnityEngine;

// Fires the Part-2 climax beats along the ride:
//   * lookBackProgress — the rider glances back over the shoulder (upper body).
//   * strikeProgress   — a visible LIGHTNING bolt beside the horse + thunder +
//                        neigh, the horse REARS and stands, and the rider is
//                        thrown OFF and FALLS to the ground.
//
// Animations are played DIRECTLY on the animators via TrailerCutsceneAnim
// (PlayableGraph) — no controller states/triggers needed, so they can't silently
// fail. Clips + mask are assigned by 'Setup Cutscene Animations'.
public class TrailerRideEvent : MonoBehaviour
{
    public TrailerHorseRide ride;
    [Range(0f, 1f)] public float lookBackProgress = 0.45f;
    [Range(0f, 1f)] public float strikeProgress = 0.9f;

    [Header("Clips (assigned by Setup Cutscene Animations)")]
    public AnimationClip lookBehindClip;
    public AnimationClip fallingBackClip;
    public AnimationClip horseRearClip;

    [Header("Per-clip rig — how each animation is allowed to drive the body")]
    [Tooltip("Look-back: upper body only, so the legs keep riding and the rider stays in the saddle.")]
    public AvatarMask upperBodyMask;
    [Tooltip("Blend weight of the glance over the riding pose. Below 1 keeps him seated; 1 fully replaces the upper body.")]
    [Range(0f, 1f)] public float lookBackWeight = 0.85f;
    [Tooltip("Fall: full body, nothing masked — he leaves the saddle entirely.")]
    public AvatarMask fallMask;
    [Tooltip("Rear-up: full body on the horse.")]
    public AvatarMask horseMask;

    [Header("Fall camera (cuts in at the strike)")]
    [Tooltip("The ride stops at the strike so the progress cutter goes quiet. This plants a low ground-level camera ahead of the horse for the rear-up and the fall.")]
    public bool useFallCamera = true;
    public float fallCamDistance = 5.5f;
    public float fallCamSide = 2.2f;
    public float fallCamHeight = 0.9f;
    public float fallCamFov = 40f;

    [Header("Sound")]
    public string neighId = "Animals/Horse_Snort";

    private TrailerLightningStrike _bolt;
    private Transform _riderGO;
    private TrailerCutsceneAnim _riderAnim, _horseAnim;
    private bool _struck, _lookedBack;

    private void OnEnable()
    {
        _struck = false; _lookedBack = false;

        if (ride != null)
        {
            // Rider = an animator that sits UNDER the horse (parented), not the horse itself.
            foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (a == null || a.transform == ride.transform) continue;
                if (a.transform.IsChildOf(ride.transform)) { _riderGO = TopUnder(a.transform, ride.transform); break; }
            }
            _horseAnim = GetOrAdd(ride.gameObject);
            if (_riderGO != null) _riderAnim = GetOrAdd(_riderGO.gameObject);
        }

        Debug.Log($"[Trailer] RideEvent ready — rider='{(_riderGO ? _riderGO.name : "NOT FOUND")}' riderAnim={_riderAnim != null} horseAnim={_horseAnim != null} | clips: look={lookBehindClip != null} fall={fallingBackClip != null} rear={horseRearClip != null} mask={upperBodyMask != null}");

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
            Debug.Log($"[Trailer] BEAT look-back (riderAnim={_riderAnim != null}, clip={lookBehindClip != null})");
            if (_riderAnim != null) _riderAnim.Play(lookBehindClip, upperBodyMask, hold: false, weight: lookBackWeight);
        }

        if (!_struck && ride.progress01 >= strikeProgress)
        {
            _struck = true;

            Vector3 gp = ride.transform.position + ride.transform.right * 3.5f;
            if (TryGround(gp, out float gy)) gp.y = gy;
            if (_bolt != null) _bolt.Strike(gp);

            if (AudioManager.Instance != null && !string.IsNullOrEmpty(neighId)) AudioManager.Instance.PlaySFX(neighId);

            Debug.Log($"[Trailer] BEAT strike (horseAnim={_horseAnim != null}, rear={horseRearClip != null}, fall={fallingBackClip != null})");
            ride.enabled = false;                                   // stop the gallop
            CutToFallCamera();
            if (_horseAnim != null) _horseAnim.Play(horseRearClip, horseMask, hold: true);   // rear + stand

            // Throw the rider off + drop beside the horse, then play the fall.
            if (_riderGO != null)
            {
                _riderGO.SetParent(null, true);
                Vector3 land = ride.transform.position - ride.transform.forward * 1.2f + ride.transform.right * 1.0f;
                if (TryGround(land, out float ly)) land.y = ly;
                _riderGO.position = land;
                _riderGO.rotation = Quaternion.LookRotation(-ride.transform.forward);
                if (_riderAnim != null) _riderAnim.Play(fallingBackClip, fallMask, hold: true);
            }
        }
    }

    // The ride stops at the strike, so the progress-driven cutter can't fire any
    // more shots. Plant a low camera on the ground ahead of the horse, looking
    // back at it, and CUT to it — the rear-up and the fall play into frame.
    private void CutToFallCamera()
    {
        if (!useFallCamera) return;

        var t = ride.transform;
        Vector3 pos = t.position + t.forward * fallCamDistance + t.right * fallCamSide;
        if (TryGround(pos, out float gy)) pos.y = gy;
        pos.y += fallCamHeight;

        var go = new GameObject("CM_Part2_Fall");
        go.transform.SetParent(transform, false);
        go.transform.position = pos;

        Vector3 look = t.position + Vector3.up * 1.2f;
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

    private static TrailerCutsceneAnim GetOrAdd(GameObject go)
    {
        return go.GetComponent<TrailerCutsceneAnim>() ?? go.AddComponent<TrailerCutsceneAnim>();
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
