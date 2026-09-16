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
    [Tooltip("Arrows embed in the player (true); magic orbs / bolts burst on impact (false).")]
    public bool stickOnHit = true;

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
        // ==== TWO SCHEDULED DESTROYS FOR ONE ARROW ====
        //
        // Awake registered a backstop Destroy and LaunchBallistic registered
        // another, so every shot queued two native destroy callbacks against the
        // same object. Harmless in effect — the second finds nothing — but it is
        // twice the bookkeeping per shot, and it makes the object impossible to
        // RECYCLE, because a scheduled Destroy cannot be cancelled.
        //
        // One timer, run in Update, which a pooled projectile can simply reset.
        _expiresAt = Time.time + Mathf.Max(lifetime, 0.5f) + 1f;
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
        _expiresAt = Time.time + lifetime;
    }

    private float _expiresAt = float.MaxValue;

    // Set by whoever owns a RECYCLED projectile. When present it is called
    // instead of Destroy, so the object goes back to its owner's pool rather
    // than to the garbage collector. Null for ordinary prefab arrows, which are
    // destroyed exactly as before.
    public System.Action<GameObject> Retire;

    // Put a reused body back into a launchable state. Everything a previous
    // flight could have left behind is cleared here; a pooled object that keeps
    // one stale field is the hardest kind of bug to find later.
    public void ResetForReuse()
    {
        launched = false;
        velocity = Vector3.zero;
        gravity = 0f;
        owner = null;
        _expiresAt = float.MaxValue;
        transform.SetParent(null, true);
        transform.localScale = _spawnScale == Vector3.zero ? transform.localScale : _spawnScale;
    }

    private Vector3 _spawnScale;

    private void Start() { if (_spawnScale == Vector3.zero) _spawnScale = transform.localScale; }

    private void Retreat()
    {
        if (Retire != null) { Retire(gameObject); return; }
        Destroy(gameObject);
    }

    private void Update()
    {
        if (Time.time >= _expiresAt) { Retreat(); return; }
        if (!launched) return;

        float dt = Time.deltaTime;
        // Integrate gravity so the path arcs and the arrow descends.
        velocity += Vector3.down * gravity * dt;

        Vector3 pos = transform.position;
        Vector3 stepVec = velocity * dt;
        float step = stepVec.magnitude;

        // ==== THE LAST UNMASKED PER-FRAME QUERY IN A GAMEPLAY SCRIPT ====
        //
        // This cast took no layer mask and no trigger override. With
        // queriesHitTriggers on globally, every arrow tested every collider on
        // every layer each frame of its flight — minimap volumes, foliage
        // triggers and the terrain's ~2500 tree colliders included — and then
        // walked a parent chain to find out it had hit scenery. Every other
        // per-frame query in the project already passes a mask.
        //
        // Only three layers can ever matter to an arrow: the player, the world
        // it stops against, and the enemies it must pass THROUGH (so the
        // pass-through test below still gets its chance to run).
        if (s_arrowMask == int.MinValue)
        {
            int m = 0;
            foreach (var n in new[] { "Default", "Obstacles", "PlayerPhysics", "Player", "Damageable" })
            {
                int l = LayerMask.NameToLayer(n);
                if (l >= 0) m |= 1 << l;
            }
            s_arrowMask = m != 0 ? m : ~0;
        }

        if (step > 0.0001f && Physics.Raycast(pos, stepVec.normalized, out RaycastHit hit, step + 0.15f,
                                              s_arrowMask, QueryTriggerInteraction.Ignore))
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

                // ==== AN ARROW THE SHIELD STOPPED BELONGS TO THE SHIELD ====
                //
                // This buried every arrow in whatever collider the cast found,
                // which for a player behind a raised guard is still the
                // CharacterController — so a shot the shield had just absorbed
                // ended up sticking out of the player's chest. That reads as the
                // block having failed, which is the exact opposite of what
                // happened, and it undermines the one defensive move the game
                // most wants the player to trust.
                //
                // PlayerBlock records what it did and on which frame, so asking
                // immediately after TakeDamage gets this hit's answer and never a
                // stale one.
                Transform stickTo = hit.collider.transform;
                var blk = PlayerBlock.Instance;
                if (blk != null && blk.LastResultFrame == Time.frameCount
                    && (blk.LastResult == PlayerBlock.Result.Blocked
                        || blk.LastResult == PlayerBlock.Result.Parried))
                {
                    // A parried arrow should not stick at all — it is knocked
                    // away, and leaving it planted in the shield turns the game's
                    // best defensive moment into a pincushion.
                    if (blk.LastResult == PlayerBlock.Result.Parried) { Impact(); return; }

                    Transform shield = blk.ShieldTransform;
                    if (shield != null) stickTo = shield;
                }

                if (stickOnHit) StickInto(stickTo, hit.point);
                else Impact();
                return;
            }

            // Pass through enemies, including the shooter; stop on world.
            // Triggers no longer need testing here — the cast ignores them.
            bool isEnemy = hit.collider.GetComponentInParent<EnemyAI>() != null;
            if (!isEnemy)
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

    private static int s_arrowMask = int.MinValue;

    private void Impact()
    {
        if (hitVFXPrefab != null) Instantiate(hitVFXPrefab, transform.position, transform.rotation);
        if (playHitSfx && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX3D(AudioID.Arrow_Hit, transform.position);
        Retreat();
    }

    // Embed the arrow in the player's body and leave it there for a few seconds
    // (per feedback) so hits feel weighty, instead of vanishing on contact.
    private void StickInto(Transform body, Vector3 point)
    {
        launched = false;   // stop integrating / raycasting
        if (playHitSfx && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX3D(AudioID.Arrow_Hit, transform.position);

        // Bury the head INTO the body. The raycast hits the CharacterController
        // capsule, which is wider than the visible mesh, so sitting at the exact
        // hit point left the arrow floating in the air beside the player. Push it
        // forward along its flight so the shaft actually sinks into the model.
        Vector3 dir = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : transform.forward;
        transform.position = point + dir * 0.35f;
        transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(-8f, 8f), 0f);

        // Ride along with whatever it hit (follows the player as they move).
        if (body != null) transform.SetParent(body, worldPositionStays: true);

        // A collider left on the stuck arrow could re-trigger things — strip it.
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // Linger, then drop off. Through the same timer as everything else, so a
        // recycled body is returned to its pool rather than destroyed out from
        // under it.
        _expiresAt = Time.time + 4f;
    }
}
