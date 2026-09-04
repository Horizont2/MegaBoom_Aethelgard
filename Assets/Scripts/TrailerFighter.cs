using UnityEngine;

// One choreographed combatant. The battle director owns it completely: where it
// stands, which way it faces, whether it is walking, and when it swings.
//
// The fight was letting EnemyAI drive the skeletons, and emergent AI is exactly
// what a staged fight must not have — each one decided for itself when to
// approach and when to attack, so they arrived at random, milled about, and stood
// still whenever their own logic had nothing to do. A trailer fight is blocked
// like a scene: everyone has a position and a job on every beat.
public class TrailerFighter : MonoBehaviour
{
    public Transform hero;
    [Tooltip("Where this one belongs right now. The director moves it; the fighter walks there and turns to face the hero.")]
    public Vector3 targetPosition;
    public float moveSpeed = 2.4f;
    public float turnSpeed = 8f;
    [Tooltip("Stop this far from the target so a group does not grind into one point.")]
    public float arriveRadius = 0.35f;

    [Header("Idle life")]
    [Tooltip("Waiting fighters sway and shift weight instead of standing frozen. A ring of statues is what makes a staged fight look unfinished.")]
    public float swayAmount = 0.18f;
    public float swaySpeed = 1.4f;

    private Animator _anim;
    private EnemyAI _ai;
    private float _phase;
    private bool _busy;      // mid-attack: the director is posing it, hands off

    private void Awake()
    {
        _anim = GetComponentInChildren<Animator>();
        _ai = GetComponent<EnemyAI>();
        _phase = Random.value * 10f;

        // Freeze the AI's own movement but keep the component alive, so its
        // damage, hit reactions, death VFX and audio all still run.
        if (_ai != null)
        {
            _ai.isCinematicFrozen = true;
            _ai.cinematicDrivesAnimator = true;
        }
        targetPosition = transform.position;
    }

    public void Attack(float impactDelay)
    {
        _busy = true;
        Trigger("Attack");
        Invoke(nameof(EndAttack), Mathf.Max(0.2f, impactDelay + 0.35f));
    }

    private void EndAttack() { _busy = false; }

    public void Trigger(string param)
    {
        if (_anim == null || _anim.runtimeAnimatorController == null || string.IsNullOrEmpty(param)) return;
        foreach (var p in _anim.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == param)
            { _anim.ResetTrigger(param); _anim.SetTrigger(param); return; }
    }

    private void Update()
    {
        if (hero == null) return;

        Vector3 to = targetPosition - transform.position; to.y = 0f;
        float dist = to.magnitude;
        bool walking = !_busy && dist > arriveRadius;

        if (walking)
        {
            transform.position += to.normalized * moveSpeed * Time.deltaTime;
        }
        else
        {
            // Never fully still. A slow weight shift is the difference between a
            // fighter waiting his turn and a prop.
            _phase += Time.deltaTime * swaySpeed;
            Vector3 side = Vector3.Cross(Vector3.up, (hero.position - transform.position).normalized);
            transform.position += side * (Mathf.Sin(_phase) * swayAmount * Time.deltaTime);
        }

        // Always face the hero: a skeleton looking the wrong way in a close-up is
        // the single most obvious tell that nobody is directing the scene.
        Vector3 look = hero.position - transform.position; look.y = 0f;
        if (look.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look.normalized),
                                                  Time.deltaTime * turnSpeed);

        if (TrailerGroundClamp.TryTerrainY(transform.position, out float gy))
        {
            var p = transform.position; p.y = gy; transform.position = p;
        }

        if (_anim != null && _anim.runtimeAnimatorController != null)
        {
            SetBool("isMoving", walking);
            SetFloat("Speed", walking ? moveSpeed : 0f);
        }
    }

    private void SetBool(string n, bool v)
    {
        foreach (var p in _anim.parameters)
            if (p.type == AnimatorControllerParameterType.Bool && p.name == n) { _anim.SetBool(n, v); return; }
    }

    private void SetFloat(string n, float v)
    {
        foreach (var p in _anim.parameters)
            if (p.type == AnimatorControllerParameterType.Float && p.name == n) { _anim.SetFloat(n, v); return; }
    }
}
