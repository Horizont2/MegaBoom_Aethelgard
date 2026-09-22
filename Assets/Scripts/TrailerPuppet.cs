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

    // ==== WHY THE GROUND IS SAMPLED AND NOT RAYCAST ====
    //
    // This used to fire a raycast against EVERY layer and take the first hit.
    // On a dressed terrain the first hit is very often not the ground: grass
    // meshes, bushes, rock props and Unity's own tree colliders all sit in the
    // way, and each is a different height. A figure walking across them lands on
    // the terrain one frame and on a shrub the next, which on screen is a crowd
    // of soldiers bouncing as though the field were a trampoline.
    //
    // Terrain.SampleHeight reads the heightmap directly. It cannot hit anything
    // that is not the ground, it is continuous rather than per-collider so the
    // walk is smooth, and it is a fraction of the cost of a physics query —
    // which matters when a hundred and seventy of these ground themselves every
    // few frames.
    [Tooltip("Read the terrain heightmap instead of raycasting. Off falls back to a raycast, for scenes whose ground is a mesh rather than a Terrain.")]
    public bool preferTerrain = true;
    [Tooltip("Layers the fallback raycast may hit. Leaving this at Everything is what puts a soldier on top of a bush.")]
    public LayerMask groundMask = ~0;

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

        // ==== ROOT MOTION ON A PUPPET IS A SECOND SET OF LEGS ====
        //
        // A puppet's position belongs to whoever is driving it. Leave root
        // motion on and the animator ALSO moves the transform, from whatever is
        // baked into the clip — and a run cycle's bake carries vertical travel,
        // so the figure bobs against the placement it was just given.
        //
        // Every other driven character in this project turns this off by hand:
        // EnemyAI, NPCGait, TrailerLegionMarch, PlayerController, the horse
        // rider. Each one had to learn it separately, which is the argument for
        // it living here instead.
        foreach (var anim in go.GetComponentsInChildren<Animator>(true))
            if (anim != null) anim.applyRootMotion = false;
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

    // ---- driven from outside -------------------------------------------------
    //
    // A crowd whose members each integrate their own position drifts: rounding,
    // frame timing and ground snapping all accumulate differently per unit, and
    // two lines that are supposed to MEET at a named moment arrive at slightly
    // different times and slightly different places. For a shot whose whole
    // point is the instant of contact, the director places every unit from one
    // number instead, and these are what it uses.

    // Position and facing, straight from the director. Grounding is left to the
    // caller so a crowd can stagger it across frames.
    // ==== THE HEIGHT IN `position` IS NOT A HEIGHT ====
    //
    // A director placing a crowd computes each position from a centre and a pair
    // of horizontal offsets, so the Y it hands over is whatever the centre
    // happened to be — one flat plane across the whole field. Writing that
    // straight into the transform and only grounding every few frames means the
    // figure sits on the plane for three frames and on the actual terrain for
    // the fourth. Where the ground differs from the plane by a metre, that is a
    // one-metre jump fifteen times a second: an army bouncing as it runs.
    //
    // So Y is never taken from the caller. It is kept from the last grounding,
    // and only the ground itself may change it.
    public void Place(Vector3 position, Vector3 forward, bool ground)
    {
        _walking = false;
        transform.position = new Vector3(position.x, transform.position.y, position.z);
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(forward.normalized);
        if (ground) SnapToGround();
    }

    public void Ground() { SnapToGround(); }

    // Scatters this puppet's animation so a crowd built from one prefab in one
    // frame does not move as one body. Two parts, and it needs both: the phase
    // is kicked to a random point of whatever state it is in, so they START out
    // of step, and the playback rate is nudged, so they cannot drift back INTO
    // step over the next few seconds.
    public void DesyncAnimation(float speedSpread)
    {
        if (_animator == null || _animator.runtimeAnimatorController == null) return;

        var state = _animator.GetCurrentAnimatorStateInfo(0);
        if (state.fullPathHash != 0) _animator.Play(state.fullPathHash, 0, Random.value);

        if (speedSpread > 0.0001f) _animator.speed = 1f + Random.Range(-speedSpread, speedSpread);
    }

    // The gait only — no movement. Written across BOTH vocabularies on purpose:
    // the skeletons run on a bool called isMoving, the hero rig on a Speed float
    // feeding a locomotion blend tree, and the Safe helpers make writing a
    // parameter a controller does not have a no-op rather than a warning. So one
    // puppet class drives both armies and neither needs to know about the other.
    public void SetGait(float speed)
    {
        _walking = false;
        SetBoolSafe("isMoving", speed > 0.05f);
        SetBoolSafe("IsGrounded", true);
        SetFloatSafe("Speed", speed);
        SetFloatSafe("MoveZ", speed);
        SetFloatSafe("MoveX", 0f);
    }

    private void SnapToGround()
    {
        Vector3 p = transform.position;

        if (preferTerrain)
        {
            Terrain[] all = Terrain.activeTerrains;
            if (all != null)
            {
                foreach (var t in all)
                {
                    if (t == null || t.terrainData == null) continue;
                    Vector3 o = t.transform.position;
                    Vector3 s = t.terrainData.size;
                    if (p.x < o.x || p.x > o.x + s.x || p.z < o.z || p.z > o.z + s.z) continue;

                    p.y = t.SampleHeight(p) + o.y + groundOffset;
                    transform.position = p;
                    return;
                }
            }
        }

        if (Physics.Raycast(p + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f, groundMask, QueryTriggerInteraction.Ignore))
        {
            p.y = hit.point.y + groundOffset;
            transform.position = p;
        }
    }

    // ---- the animator rule --------------------------------------------------

    private void SetBoolSafe(string param, bool value)
    {
        if (_animator == null || !HasParam(param, AnimatorControllerParameterType.Bool)) return;
        _animator.SetBool(param, value);
    }

    private void SetFloatSafe(string param, float value)
    {
        if (_animator == null || !HasParam(param, AnimatorControllerParameterType.Float)) return;
        _animator.SetFloat(param, value);
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
