using UnityEngine;

// Enemy projectile (arrow / bolt) fired by EnemyAI ranged mode. Flies a real
// BALLISTIC ARC (gravity), tilting to follow its trajectory so it reads like a
// real arrow. Raycasts each frame so fast arrows don't tunnel through the
// player, deals damage on hit, and self-destructs on impact / world hit / after
// its lifetime. Passes through other enemies and triggers. No Collider needed.
public class EnemyProjectile : MonoBehaviour
{
    [Tooltip("Seconds before the arrow despawns if it hits nothing.")]
    public float lifetime = 6f;
    [Tooltip("Optional impact VFX spawned where the arrow lands.")]
    public GameObject hitVFXPrefab;
    public bool playHitSfx = true;

    private Vector3 velocity;
    private float gravity;
    private float damage;
    private GameObject owner;
    private bool launched;

    private void Awake()
    {
        // If the prefab carries a Rigidbody, keep it kinematic + no gravity so
        // physics never fights our transform move (that made arrows hang / arc
        // oddly). Movement is driven purely by our own integration below.
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
        // Hard backstop: never let a mis-fired arrow hang forever.
        Destroy(gameObject, Mathf.Max(lifetime, 0.5f) + 1f);
    }

    // Straight-line shot (kept for compatibility / non-arcing bolts).
    public void Launch(Vector3 dir, float speed, float dmg, GameObject source)
    {
        LaunchBallistic(dir.normalized * speed, 0f, dmg, source);
    }

    // Ballistic shot: initial velocity + gravity. EnemyAI computes the velocity
    // that arcs to the player, so the arrow lobs and drops naturally.
    public void LaunchBallistic(Vector3 initialVelocity, float grav, float dmg, GameObject source)
    {
        velocity = initialVelocity;
        gravity = grav;
        damage = dmg;
        owner = source;
        launched = true;
        if (velocity.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(velocity);
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        if (!launched) return;

        float dt = Time.deltaTime;
        // Integrate gravity so the path arcs and the arrow descends.
        velocity += Vector3.down * gravity * dt;

        Vector3 pos = transform.position;
        Vector3 stepVec = velocity * dt;
        float step = stepVec.magnitude;

        if (step > 0.0001f && Physics.Raycast(pos, stepVec.normalized, out RaycastHit hit, step + 0.15f))
        {
            PlayerController pc = hit.collider.GetComponentInParent<PlayerController>();
            if (pc != null)
            {
                pc.TakeDamage(new DamageInfo
                {
                    Amount = damage,
                    PushDirection = velocity.normalized,
                    SourceName = "Archer"
                });
                Impact();
                return;
            }

            // Pass through triggers and enemies (incl. the shooter); stop on world.
            bool isEnemy = hit.collider.GetComponentInParent<EnemyAI>() != null;
            if (!hit.collider.isTrigger && !isEnemy)
            {
                Impact();
                return;
            }
        }

        transform.position = pos + stepVec;
        // Tilt the arrow to follow its arc.
        if (velocity.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(velocity);
    }

    private void Impact()
    {
        if (hitVFXPrefab != null) Instantiate(hitVFXPrefab, transform.position, transform.rotation);
        if (playHitSfx && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Attack, transform.position);
        Destroy(gameObject);
    }
}
