using UnityEngine;

// Fires the Part-2 climax beats at a point along the ride: a LIGHTNING flash
// (a bright directional burst — reads as a strike) + a thunder crack + the horse
// NEIGH. Progress-driven so it lands at the same spot on the route every time.
//
// NOTE: the horse REAR-UP, the rider LOOK-BACK / FALL and the battle need real
// animations (see the animation list) — this component covers the strike, sound
// and (optionally) a stylised transform-based rear as a placeholder.
public class TrailerRideEvent : MonoBehaviour
{
    public TrailerHorseRide ride;
    [Range(0f, 1f)] public float lookBackProgress = 0.45f;   // rider glances back (fear)
    [Range(0f, 1f)] public float strikeProgress = 0.9f;      // lightning + rear + fall

    [Header("Sound (routes through AudioManager)")]
    public string thunderId = "AMB/AMB_Thunder";
    public string neighId = "Animals/Horse_Snort";   // placeholder until a real neigh event exists

    [Header("Placeholder rear (only if the horse rear anim isn't wired)")]
    public bool fakeRear = false;
    public Transform horseModel;
    public float rearAngle = 45f;
    public float rearTime = 0.6f;

    private TrailerLightningStrike _bolt;
    private Animator _horseAnim, _riderAnim;
    private bool _struck, _lookedBack;
    private float _rearT = -1f;
    private Quaternion _rearBase;

    private void OnEnable()
    {
        _struck = false; _lookedBack = false; _rearT = -1f;
        if (ride != null) _horseAnim = ride.GetComponent<Animator>() ?? ride.GetComponentInChildren<Animator>();
        var rider = GameObject.FindGameObjectWithTag("Player");
        if (rider != null) _riderAnim = rider.GetComponentInChildren<Animator>();
        if (_bolt == null)
        {
            var go = new GameObject("Trailer_LightningBolt");
            go.transform.SetParent(transform, false);
            go.AddComponent<LineRenderer>();
            _bolt = go.AddComponent<TrailerLightningStrike>();
            _bolt.thunderId = thunderId;
        }
    }

    private void Update()
    {
        if (ride == null) return;

        // Rider glances back over the shoulder (upper body only) — dread.
        if (!_lookedBack && ride.progress01 >= lookBackProgress)
        {
            _lookedBack = true;
            if (_riderAnim != null) _riderAnim.SetTrigger("LookBack");
        }

        if (!_struck && ride.progress01 >= strikeProgress)
        {
            _struck = true;
            // Strike the ground a few metres BESIDE the horse.
            Vector3 side = ride.transform.right * 3.5f;
            Vector3 gp = ride.transform.position + side;
            if (TryGround(gp, out float gy)) gp.y = gy;
            if (_bolt != null) _bolt.Strike(gp);

            var am = AudioManager.Instance;
            if (am != null && !string.IsNullOrEmpty(neighId)) am.PlaySFX(neighId);

            // Real rear-up (horse) + the rider is thrown (fall) — wired by
            // 'Setup Cutscene Animations'.
            if (_horseAnim != null) _horseAnim.SetTrigger("Rear");
            if (_riderAnim != null) _riderAnim.SetTrigger("Fall");

            if (fakeRear)
            {
                if (horseModel == null) horseModel = ride.transform;
                if (horseModel != null) { _rearBase = horseModel.localRotation; _rearT = 0f; }
            }
        }

        if (_rearT >= 0f && horseModel != null)
        {
            _rearT += Time.deltaTime;
            float t = Mathf.Clamp01(_rearT / Mathf.Max(0.01f, rearTime));
            float pitch = Mathf.Sin(t * Mathf.PI) * rearAngle;      // up then down
            horseModel.localRotation = _rearBase * Quaternion.Euler(-pitch, 0f, 0f);
            if (_rearT >= rearTime) { _rearT = -1f; horseModel.localRotation = _rearBase; }
        }
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
