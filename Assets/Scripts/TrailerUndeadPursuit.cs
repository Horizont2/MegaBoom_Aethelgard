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

    private enum State { Buried, Rising, Chasing }
    private State _state = State.Buried;
    private Animator _anim;
    private Vector3 _ground;
    private float _riseT;

    private void Start()
    {
        _anim = GetComponentInChildren<Animator>();
        _ground = transform.position;
        transform.position = _ground - Vector3.up * riseDepth;   // sink underground
        if (_anim != null) _anim.SetBool("isMoving", false);
    }

    private void Update()
    {
        if (target == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) target = p.transform; else return;
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
                Vector3 to = target.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.6f)
                {
                    transform.position += to.normalized * chaseSpeed * Time.deltaTime;
                    Face();
                }
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
