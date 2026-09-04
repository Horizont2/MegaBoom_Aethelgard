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
    public AvatarMask upperBodyMask;

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
            if (_riderAnim != null) _riderAnim.Play(lookBehindClip, upperBodyMask, hold: false);   // upper body only
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
            if (_horseAnim != null) _horseAnim.Play(horseRearClip, null, hold: true);   // rear + stand

            // Throw the rider off + drop beside the horse, then play the fall.
            if (_riderGO != null)
            {
                _riderGO.SetParent(null, true);
                Vector3 land = ride.transform.position - ride.transform.forward * 1.2f + ride.transform.right * 1.0f;
                if (TryGround(land, out float ly)) land.y = ly;
                _riderGO.position = land;
                _riderGO.rotation = Quaternion.LookRotation(-ride.transform.forward);
                if (_riderAnim != null) _riderAnim.Play(fallingBackClip, null, hold: true);
            }
        }
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
