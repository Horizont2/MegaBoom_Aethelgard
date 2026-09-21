using UnityEngine;
using UnityEngine.AI;

// A skeleton that acts, instead of one that decides.
//
// ==== WHY NOT JUST SPAWN ENEMIES ====
//
// Every previous attempt at a populated trailer shot spawned real enemies and let
// EnemyAI drive them, and every one of them produced the same footage: figures
// scattering in all directions, running sideways because a look-rotation fought a
// navmesh heading, health bars and minimap dots floating over everything, and the
// occasional one sprinting straight at the lens because it had aggroed on the
// player standing two hundred metres away.
//
// None of that is a bug in EnemyAI. It is EnemyAI working — reacting, pathing,
// deciding — inside a shot where nothing is supposed to decide anything. A take
// has to be the same every time you run it, and an agent with opinions cannot
// give you that.
//
// So a puppet is an enemy with the agent taken out: the model, the animator and
// nothing else. It stands where it is put, walks in the direction it is given,
// and turns when it is told. Thirty lines of behaviour, fully deterministic.
//
// ==== THE ANIMATOR RULE ====
//
// Parameters are written through the Safe helpers, which check the controller
// actually has them. Writing a parameter that does not exist logs a warning EVERY
// FRAME, per object; an earlier trailer script did exactly that across a crowd and
// produced enough console traffic to lock up a machine hard enough to need a
// reboot. The check is two lines and it is never optional.
[DisallowMultipleComponent]
public class TrailerPuppet : MonoBehaviour
{
    private Animator _animator;
    private Vector3 _walkDir;
    private float _walkSpeed;
    private bool _walking;

    private bool _turning;
    private Quaternion _turnFrom, _turnTo;
    private float _turnStart, _turnDuration;

    [Tooltip("Metres above the sampled ground. Most skeleton prefabs have their pivot at the feet, so 0 is right.")]
    public float groundOffset = 0f;

    // Build a puppet from an enemy prefab: instantiate it, then take out
    // everything that would make it a participant rather than a prop.
    public static TrailerPuppet Spawn(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent)
    {
        if (prefab == null) return null;

        var go = Instantiate(prefab, pos, rot, parent);
        Strip(go);

        var p = go.AddComponent<TrailerPuppet>();
        p._animator = go.GetComponentInChildren<Animator>();
        p.SnapToGround();
        p.Stand();
        return p;
    }

    // DestroyImmediate, not Destroy, and that distinction matters here.
    //
    // Destroy is deferred to the end of the frame, and a component queued for
    // destruction still gets its Start() called first. For EnemyAI that Start is
    // where it registers itself, wakes its navmesh agent and switches its health
    // canvas on — so a "destroyed" AI would still spend one frame doing all the
    // things this method exists to prevent, on a puppet that is about to be
    // filmed. Removing it immediately means that Start never runs at all.
    // Public so a director that drives its own crowd can borrow it without
    // reimplementing the list - and every reimplementation of this list has so
    // far missed the same two entries, the runtime minimap renderer and the
    // health canvas, which are exactly the two that show up on camera.
    public static void Strip(GameObject go)
    {
        if (go == null) return;

        // A boss carries a different brain, and its Start is the one that puts
        // the BOSS HEALTH BAR on screen - over the trailer.
        foreach (var boss in go.GetComponentsInChildren<TutorialBossAI>(true))
            if (boss != null) DestroyImmediate(boss);

        // Read what the AI knows before taking it away.
        GameObject healthCanvas = null;
        var ai = go.GetComponent<EnemyAI>();
        if (ai != null)
        {
            healthCanvas = ai.healthCanvas;
            ai.suppressWorldHealthBar = true;
            DestroyImmediate(ai);
        }

        foreach (var agent in go.GetComponentsInChildren<NavMeshAgent>(true)) DestroyImmediate(agent);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;

        // The health bar, and any other world-space UI riding on the model.
        if (healthCanvas != null) healthCanvas.SetActive(false);
        foreach (var canvas in go.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);

        // The minimap dot is a render layer, not a component: EnemyAI puts a
        // dedicated renderer on the MinimapOnly layer. Switching those off is
        // what actually removes the marker.
        int minimapLayer = LayerMask.NameToLayer("MinimapOnly");
        if (minimapLayer >= 0)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.layer == minimapLayer) r.enabled = false;
        }
    }

    // ---- direction ----------------------------------------------------------

    public void Stand()
    {
        _walking = false;
        SetBoolSafe("isMoving", false);
    }

    public void WalkAlong(Vector3 direction, float speed)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) { Stand(); return; }

        _walkDir = direction.normalized;
        _walkSpeed = speed;
        _walking = true;
        transform.rotation = Quaternion.LookRotation(_walkDir);
        SetBoolSafe("isMoving", true);
    }

    public void FaceTowards(Vector3 worldPoint)
    {
        Vector3 to = worldPoint - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(to.normalized);
    }

    // The shot's reversal: a figure that has been still for fifteen seconds turns
    // its whole body to the lens. Deliberately faster than a person could manage —
    // the unnatural speed of the turn is the point, and it also means a rig with no
    // head bone to isolate still reads as "it noticed".
    public void TurnTo(Vector3 worldPoint, float seconds)
    {
        Vector3 to = worldPoint - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.001f) return;

        _turnFrom = transform.rotation;
        _turnTo = Quaternion.LookRotation(to.normalized);
        _turnStart = Time.unscaledTime;
        _turnDuration = Mathf.Max(0.05f, seconds);
        _turning = true;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (_walking && _walkSpeed > 0f)
        {
            transform.position += _walkDir * (_walkSpeed * dt);
            SnapToGround();
        }

        if (_turning)
        {
            float k = Mathf.Clamp01((Time.unscaledTime - _turnStart) / _turnDuration);
            // Ease out only: it snaps into the turn and settles, which reads as a
            // reaction. Easing in as well would read as a slow, considered look.
            float e = 1f - (1f - k) * (1f - k);
            transform.rotation = Quaternion.Slerp(_turnFrom, _turnTo, e);
            if (k >= 1f) _turning = false;
        }
    }

    private void SnapToGround()
    {
        Vector3 p = transform.position;
        if (Physics.Raycast(p + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore))
        {
            p.y = hit.point.y + groundOffset;
            transform.position = p;
            return;
        }

        Terrain[] all = Terrain.activeTerrains;
        if (all == null) return;
        foreach (var t in all)
        {
            if (t == null || t.terrainData == null) continue;
            Vector3 o = t.transform.position;
            Vector3 s = t.terrainData.size;
            if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z)
            {
                p.y = t.SampleHeight(p) + o.y + groundOffset;
                transform.position = p;
                return;
            }
        }
    }

    // ---- the animator rule --------------------------------------------------

    private void SetBoolSafe(string param, bool value)
    {
        if (_animator == null || !HasParam(param, AnimatorControllerParameterType.Bool)) return;
        _animator.SetBool(param, value);
    }

    private bool HasParam(string param, AnimatorControllerParameterType type)
    {
        if (_animator.runtimeAnimatorController == null) return false;
        var ps = _animator.parameters;
        for (int i = 0; i < ps.Length; i++)
            if (ps[i].type == type && ps[i].name == param) return true;
        return false;
    }
}
