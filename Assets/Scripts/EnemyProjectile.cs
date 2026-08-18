using UnityEngine;

// Straight-line enemy projectile (arrow / bolt) fired by EnemyAI ranged mode.
// Raycasts each frame so fast arrows don't tunnel through the player, deals
// damage on hit, and self-destructs on impact, on hitting the world, or after
// its lifetime. Ignores other enemies so archers don't shoot their own crowd.
// Hit detection is raycast-based, so no Collider is required on the arrow.
public class EnemyProjectile : MonoBehaviour
{
    [Tooltip("Seconds before the arrow despawns if it hits nothing.")]
    public float lifetime = 5f;
    [Tooltip("Optional impact VFX spawned where the arrow lands.")]
    public GameObject hitVFXPrefab;
    [Tooltip("Optional flight SFX (FMOD id name) — leave blank for none.")]
    public bool playHitSfx = true;

    private Vector3 velocity;
    private float damage;
    private GameObject owner;
    private bool launched;

    // Called by EnemyAI.FireProjectile right after spawn.
    public void Launch(Vector3 dir, float speed, float dmg, GameObject source)
    {
        velocity = dir.normalized * speed;
        damage = dmg;
        owner = source;
        launched = true;
        if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir);
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        if (!launched) return;

        float step = velocity.magnitude * Time.deltaTime;
        Vector3 pos = transform.position;

        if (Physics.Raycast(pos, velocity.normalized, out RaycastHit hit, step + 0.15f))
        {
            // Player hit?
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

            // Pass through trigger volumes and other enemies (incl. the shooter),
            // stop on solid world geometry.
            bool isEnemy = hit.collider.GetComponentInParent<EnemyAI>() != null;
            if (!hit.collider.isTrigger && !isEnemy)
            {
                Impact();
                return;
            }
        }

        transform.position = pos + velocity * Time.deltaTime;
    }

    private void Impact()
    {
        if (hitVFXPrefab != null) Instantiate(hitVFXPrefab, transform.position, transform.rotation);
        if (playHitSfx && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Attack, transform.position);
        Destroy(gameObject);
    }
}
