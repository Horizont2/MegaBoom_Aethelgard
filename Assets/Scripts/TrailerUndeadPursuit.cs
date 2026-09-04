using UnityEngine;

// Cinematic undead pursuit for the trailer. Each skeleton starts BURIED just
// under the ground. When the horse has passed it (the skeleton is behind the
// rider and within range) it ERUPTS from the earth and RUNS after him — selling
// a chase that builds toward the battle. Uses the skeleton's own run animation
// (the game drives it via isMoving / Speed).
public class TrailerUndeadPursuit : MonoBehaviour
{
    public Transform target;             // the horse / rider
    [Tooltip("Rise once the rider is within this range AND has passed the skeleton.")]
    public float triggerRange = 16f;
    public float riseDepth = 1.8f;
    public float riseTime = 0.7f;
    public float chaseSpeed = 4.5f;
    public float turnSpeed = 6f;
    [Tooltip("Stop chasing this close so they don't run INTO the horse.")]
    public float stopDistance = 2.5f;

    [Header("Chase feel")]
    [Tooltip("They must never outrun the horse — the chase is a threat behind him, not a swarm around him. Their speed is capped at this fraction of the horse's ACTUAL speed.")]
    [Range(0.3f, 1f)] public float maxSpeedFactor = 0.88f;
    [Tooltip("Distance they settle at behind the rider. Closer than this and they ease off, so they read as a pursuing horde rather than NPCs glued to the horse.")]
    public float trailDistance = 9f;
    [Tooltip("Sideways spread so a group doesn't collapse into one file.")]
    public float lateralSpread = 3.5f;

    private enum State { Buried, Rising, Chasing }
    private State _state = State.Buried;
    private Animator _anim;
    private Vector3 _ground;
    private float _riseT;
    private float _lane;                 // this skeleton's sideways slot in the horde
    private Vector3 _targetLastPos;
    private float _targetSpeed;

    private void Start()
    {
        _anim = GetComponentInChildren<Animator>();
        _ground = transform.position;
        transform.position = _ground - Vector3.up * riseDepth;   // sink underground
        if (_anim != null) _anim.SetBool("isMoving", false);
        _lane = Random.Range(-lateralSpread, lateralSpread);
    }

    private void Update()
    {
        if (target == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) { target = p.transform; _targetLastPos = target.position; }
            else return;
        }

        // Measure how fast the rider is ACTUALLY moving, so the pursuit can be
        // capped below it whatever the spline length turns out to be.
        if (Time.deltaTime > 0.0001f)
        {
            Vector3 d = target.position - _targetLastPos; d.y = 0f;
            _targetSpeed = Mathf.Lerp(_targetSpeed, d.magnitude / Time.deltaTime, 0.2f);
            _targetLastPos = target.position;
        }

        switch (_state)
        {
            case State.Buried:
                Vector3 rel = _ground - target.position;      // skeleton relative to rider
                rel.y = 0f;
                bool behind = Vector3.Dot(target.forward, rel) < 0f;   // rider has passed it
                if (behind && rel.magnitude < triggerRange) { _state = State.Rising; _riseT = 0f; }
                break;

            case State.Rising:
                _riseT += Time.deltaTime;
                float k = Mathf.Clamp01(_riseT / Mathf.Max(0.01f, riseTime));
                transform.position = Vector3.Lerp(_ground - Vector3.up * riseDepth, _ground, k);
                Face();
                if (k >= 1f)
                {
                    _state = State.Chasing;
                    if (_anim != null) { _anim.SetBool("isMoving", true); _anim.SetFloat("Speed", chaseSpeed); }
                }
                break;

            case State.Chasing:
                // Aim at a point BEHIND the rider, offset sideways per skeleton,
                // so the horde trails him in a spread instead of piling onto him.
                Vector3 anchor = target.position - target.forward * trailDistance + target.right * _lane;
                Vector3 to = anchor - transform.position; to.y = 0f;
                float dToRider = Vector3.Distance(
                    new Vector3(target.position.x, 0f, target.position.z),
                    new Vector3(transform.position.x, 0f, transform.position.z));

                // Never faster than the horse: measured from his real movement, so
                // it holds whatever the spline length works out to.
                float cap = _targetSpeed > 0.2f ? _targetSpeed * maxSpeedFactor : chaseSpeed;
                float speed = Mathf.Min(chaseSpeed, cap);
                // Ease off completely once we are at the trailing distance.
                if (dToRider < stopDistance) speed = 0f;
                else if (to.magnitude < 0.6f) speed *= to.magnitude / 0.6f;

                if (speed > 0.01f && to.sqrMagnitude > 0.0001f)
                    transform.position += to.normalized * speed * Time.deltaTime;

                if (_anim != null) _anim.SetFloat("Speed", speed);
                Face();
                if (TryGround(transform.position, out float gy)) { var p = transform.position; p.y = gy; transform.position = p; }
                break;
        }
    }

    private void Face()
    {
        if (target == null) return;
        Vector3 d = target.position - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), Time.deltaTime * turnSpeed);
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
