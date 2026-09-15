using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Lightweight friendly companion — e.g. a freed caged mercenary. Follows the
// player, seeks the nearest enemy within aggro range, closes to melee and
// attacks on cooldown. Enemies in this game target the PLAYER (not allies), so
// the companion is a pure damage helper; it leaves after allyLifetime seconds
// (0 = stays until the scene ends). Drives animator params isMoving (bool) and
// Attack (trigger) if the model has them.
public class AllyAI : MonoBehaviour, IDamageable
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float followDistance = 3f;
    [Tooltip("Once following, the ally keeps closing until it is this near. Must be comfortably below followDistance or the companion vibrates on the threshold.")]
    public float followStopDistance = 1.6f;
    public float aggroRange = 12f;

    [Header("Combat")]
    public float attackRange = 1.8f;
    [Tooltip("Seconds between the START of one swing and the next. Must be >= the attack clip length or the ally re-triggers mid-swing and deals two hits per animation.")]
    public float attackCooldown = 1.4f;
    public float damage = 20f;
    [Tooltip("Delay from the swing starting to the hit landing — should match the weapon-contact frame of the attack clip.")]
    public float attackImpactDelay = 0.9f;
    [Tooltip("Length of the attack animation; the ally is locked from re-attacking for this long so one windup = one hit.")]
    public float attackClipLength = 1.4f;
    private bool isAttacking;

    [Header("Survivability")]
    // 60 was too little to matter. A freed captive that dies to the first pack
    // is a reward the player watches evaporate — and since the retaliation below
    // charges per adjacent enemy, being useful in a fight was itself what killed
    // it fastest.
    [Tooltip("The ally CAN die — it isn't invincible. High enough to survive the fight it was freed into, low enough that a crowd still finishes it.")]
    public float maxHealth = 180f;
    [Tooltip("Chip damage per second the ally takes for EACH enemy in melee range — so a lone ally worn down by a crowd eventually falls.")]
    public float meleeRetaliationDPS = 4f;
    public GameObject deathVFXPrefab;
    private float currentHealth;
    private bool dead;

    public float Health => currentHealth;
    public float HealthMax => Mathf.Max(1f, maxHealth);
    public float Health01 => Mathf.Clamp01(currentHealth / HealthMax);
    public bool IsDead => dead;

    [Header("Health bar")]
    [Tooltip("Draw a small health bar above the ally. Built in code, so no prefab wiring is needed.")]
    public bool showHealthBar = true;
    [Tooltip("Metres above the ally's origin the bar floats.")]
    public float healthBarHeight = 2.3f;
    [Tooltip("Width of the bar in world units.")]
    public float healthBarWidth = 1.1f;

    [Header("Campfire")]
    [Tooltip("Heal beside a campfire the way the player does.")]
    public bool healAtCampfires = true;
    [Tooltip("Health per second regained inside a campfire's radius.")]
    public float campfireHealPerSecond = 14f;
    [Tooltip("Below this fraction of maximum health the ally will BREAK OFF and go warm itself — but only when no enemy is in aggro range. A companion that walks away mid-fight is worse than one that dies.")]
    [Range(0f, 0.9f)] public float seekFireBelowHealth = 0.45f;
    [Tooltip("Furthest the ally will travel to reach a fire. Beyond this it stays with the player and takes its chances.")]
    public float campfireSeekRange = 26f;

    private CampfireInteract _fire;
    private float _fireScan;

    [Header("Lifetime")]
    [Tooltip("Seconds the ally fights before leaving. 0 = stays until it dies / the scene ends.")]
    public float allyLifetime = 45f;
    public GameObject leaveVFXPrefab;

    private Transform player;
    private Animator animator;
    private float lastAttackTime;
    private float bornTime;
    private bool leaving;
    private static readonly Collider[] s_buf = new Collider[24];

    // Global registry so enemies can find nearby allies cheaply (no per-enemy
    // FindObjectsByType). Kept in sync via OnEnable/OnDisable.
    public static readonly List<AllyAI> Active = new List<AllyAI>();

    private void OnEnable()
    {
        bornTime = Time.time;
        lastAttackTime = -999f;
        currentHealth = maxHealth;
        dead = false;
        if (!Active.Contains(this)) Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
    }

    // IDamageable — grenades, boss AoE, and any area attack that hits the ally's
    // spot wear it down. Combined with melee retaliation, this lets the ally die.
    public void TakeDamage(DamageInfo info)
    {
        if (dead) return;
        currentHealth -= Mathf.Max(0f, info.Amount);
        if (currentHealth <= 0f) Die();
    }

    private void Die()
    {
        if (dead) return;
        dead = true;
        if (deathVFXPrefab != null) Instantiate(deathVFXPrefab, transform.position + Vector3.up, Quaternion.identity);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Die, transform.position);
        Destroy(gameObject);
    }

    private void Start()
    {
        animator = GetComponentInChildren<Animator>();
        CachePlayer();
        if (showHealthBar) AllyHealthBar.Attach(this);
    }

    private void CachePlayer()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
    }

    private void Update()
    {
        if (leaving || dead) return;
        if (allyLifetime > 0f && Time.time - bornTime > allyLifetime) { StartCoroutine(LeaveRoutine()); return; }
        if (player == null) { CachePlayer(); if (player == null) return; }

        // Retaliation: take chip damage for each enemy pressed into melee range,
        // so a lone ally caught by a crowd is worn down and can fall.
        if (meleeRetaliationDPS > 0f)
        {
            int adj = 0;
            int m = Physics.OverlapSphereNonAlloc(transform.position, attackRange + 0.6f, s_buf, 1 << 9);
            for (int i = 0; i < m; i++)
                if (s_buf[i] != null && (s_buf[i].GetComponentInParent<EnemyAI>() != null || s_buf[i].GetComponentInParent<TutorialBossAI>() != null)) adj++;
            if (adj > 0)
            {
                currentHealth -= meleeRetaliationDPS * adj * Time.deltaTime;
                if (currentHealth <= 0f) { Die(); return; }
            }
        }

        TickCampfireHealing();

        if (isAttacking) { SetMoving(false); return; } // one swing at a time — no double-hit

        Component enemy = FindNearestEnemy();
        if (enemy != null)
        {
            Vector3 ep = enemy.transform.position;
            FaceToward(ep);
            if (FlatDist(transform.position, ep) <= attackRange)
            {
                SetMoving(false);
                if (Time.time >= lastAttackTime + attackCooldown)
                {
                    lastAttackTime = Time.time;
                    StartCoroutine(AttackRoutine(enemy));
                }
            }
            else { MoveToward(ep, attackRange * 0.8f); SetMoving(true); }
        }
        else
        {
            // ==== WARM ITSELF, BUT ONLY WHEN THE FIGHT IS OVER ====
            //
            // A companion that breaks off mid-fight to go stand by a fire is
            // worse than one that dies fighting, so this branch is only reached
            // when FindNearestEnemy came back empty. Enemies always win.
            if (healAtCampfires && Health01 < seekFireBelowHealth)
            {
                var fire = NearestFire();
                if (fire != null)
                {
                    float dFire = FlatDist(transform.position, fire.transform.position);
                    if (dFire > fire.healRadius * 0.6f)
                    {
                        MoveToward(fire.transform.position);
                        FaceToward(fire.transform.position);
                        SetMoving(true);
                        return;
                    }
                    // At the fire: stand still and recover.
                    SetMoving(false);
                    return;
                }
            }

            // ==== A BARE DISTANCE THRESHOLD MAKES A COMPANION VIBRATE ====
            //
            // "Move if further than followDistance, stand still otherwise" reads
            // fine and shakes on screen. While the player runs, the gap sits
            // right on the threshold, so the ally steps at full speed on one
            // frame and freezes on the next, forever — and the locomotion blend
            // snaps between run and idle along with it.
            //
            // Hysteresis: once it has set off it keeps closing until it is well
            // inside, and it does not set off again until the player is properly
            // away. The arrival ramp in MoveToward does the rest.
            float dPlayer = FlatDist(transform.position, player.position);
            _following = dPlayer > (_following ? followStopDistance : followDistance);

            if (_following)
            {
                MoveToward(player.position, followStopDistance);
                FaceToward(player.position);
                SetMoving(true);
            }
            else SetMoving(false);
        }
    }

    // Healing happens wherever the ally is standing, whether it walked there on
    // purpose or the fight simply happened next to a fire.
    private void TickCampfireHealing()
    {
        if (!healAtCampfires || dead || currentHealth >= maxHealth) return;

        var fire = NearestFire();
        if (fire == null) return;
        if (FlatDist(transform.position, fire.transform.position) > fire.healRadius) return;

        currentHealth = Mathf.Min(maxHealth, currentHealth + campfireHealPerSecond * Time.deltaTime);
    }

    // Cached, because scanning every campfire in the scene every frame for a
    // companion that mostly does not need one is pure waste.
    private CampfireInteract NearestFire()
    {
        _fireScan -= Time.deltaTime;
        if (_fireScan <= 0f)
        {
            _fireScan = 1f;
            _fire = null;
            float best = campfireSeekRange * campfireSeekRange;
            foreach (var f in FindObjectsByType<CampfireInteract>(FindObjectsSortMode.None))
            {
                if (f == null) continue;
                float sq = (f.transform.position - transform.position).sqrMagnitude;
                if (sq < best) { best = sq; _fire = f; }
            }
        }
        return _fire;
    }

    // One full swing: lock out re-attacking for the clip length, trigger the
    // anim, land exactly ONE hit at the contact frame. This fixes the "winds up
    // once but hits twice" bug (the 2.4s clip fit two 1.2s cooldowns).
    private IEnumerator AttackRoutine(Component target)
    {
        isAttacking = true;
        if (animator != null) animator.SetTriggerSafe("Attack");

        yield return new WaitForSeconds(attackImpactDelay);

        // Component == null uses Unity's lifetime check, so a target destroyed
        // mid-swing is safely skipped.
        if (target != null && target is IDamageable dmg && !dead)
        {
            dmg.TakeDamage(new DamageInfo { Amount = damage, PushDirection = transform.forward, SourceName = "Ally" });
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Attack, transform.position);
        }

        // Hold the lock until the swing animation is actually finished.
        float rest = Mathf.Max(0f, attackClipLength - attackImpactDelay);
        if (rest > 0f) yield return new WaitForSeconds(rest);
        isAttacking = false;
    }

    // Nearest enemy on the enemy layer (9). Returns the EnemyAI/TutorialBossAI
    // component (both are IDamageable) so the caller can null-check its lifetime.
    private Component FindNearestEnemy()
    {
        Component best = null;
        float bestSqr = aggroRange * aggroRange;
        int n = Physics.OverlapSphereNonAlloc(transform.position, aggroRange, s_buf, 1 << 9);
        for (int i = 0; i < n; i++)
        {
            Collider c = s_buf[i];
            if (c == null) continue;

            Component cand = c.GetComponentInParent<EnemyAI>();
            if (cand == null) cand = c.GetComponentInParent<TutorialBossAI>();
            if (cand == null) continue;

            float sq = (cand.transform.position - transform.position).sqrMagnitude;
            if (sq < bestSqr) { bestSqr = sq; best = cand; }
        }
        return best;
    }

    private float _curSpeed; // ramped, for non-robotic accel/decel
    private bool _following; // hysteresis latch — see the follow branch

    // stopAt: how close is close enough. Speed tapers over the last stretch and
    // the step is clamped so a single frame can never carry the ally past the
    // destination and into whatever is standing on it.
    private void MoveToward(Vector3 dest, float stopAt = 0f)
    {
        Vector3 dir = dest - transform.position; dir.y = 0f;
        float d = dir.magnitude;
        if (d < 0.001f) return;
        dir /= d;
        // Remembered for the locomotion blend — see SetMoving.
        _lastMoveDir = dir;

        // Ease speed up/down instead of snapping to full velocity instantly,
        // and back off on approach so it settles rather than overshooting.
        float target = moveSpeed;
        if (stopAt > 0f) target *= Mathf.Clamp01((d - stopAt) / 1.2f);
        _curSpeed = Mathf.MoveTowards(_curSpeed, target, moveSpeed * 3f * Time.deltaTime);

        float step = Mathf.Min(_curSpeed * Time.deltaTime, Mathf.Max(0f, d - stopAt));
        if (step <= 0f) return;

        Vector3 next = transform.position + dir * step;
        next.y = GroundY(next);
        transform.position = next;
    }

    private void FaceToward(Vector3 p)
    {
        Vector3 dir = p - transform.position; dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 12f * Time.deltaTime);
    }

    // ==== THE ALLY RUNS ON THE PLAYER'S ANIMATOR ====
    //
    // The caged ally is the Barbarian prefab, and its controller is
    // HeroAnimator — the player's. That controller has no "isMoving" bool at
    // all; its Locomotion blend is driven by MoveX and MoveZ, with Speed on top.
    // This set isMoving (a no-op, the parameter does not exist) and Speed, and
    // never touched MoveX/MoveZ — so the blend tree stayed at its origin and the
    // ally slid around in an idle pose.
    //
    // Both conventions are still driven, because Set…Safe no-ops on a missing
    // parameter and a different companion may yet use a simpler controller.
    private Vector3 _lastMoveDir;

    private void SetMoving(bool m)
    {
        if (animator == null) return;
        if (!m) _curSpeed = Mathf.MoveTowards(_curSpeed, 0f, moveSpeed * 4f * Time.deltaTime);

        animator.SetBoolSafe("isMoving", m);
        animator.SetBoolSafe("IsGrounded", true);

        // Ride the ramp down instead of hard-zeroing the blend the instant the
        // ally stops. Slamming Speed to 0 on the same frame the transform stops
        // is half of what made the stop-start threshold look like a vibration.
        float speed01 = moveSpeed > 0.01f ? Mathf.Clamp01(_curSpeed / moveSpeed) : 0f;
        animator.SetFloatSafe("Speed", speed01);

        // Movement in the ally's OWN space, which is what a 2D locomotion blend
        // expects: +Z forward, +X to its right. Kept pointing the same way while
        // decelerating, so the blend eases out along the direction it was going.
        Vector3 local = transform.InverseTransformDirection(_lastMoveDir);
        animator.SetFloatSafe("MoveX", Mathf.Clamp(local.x, -1f, 1f) * speed01);
        animator.SetFloatSafe("MoveZ", Mathf.Clamp(local.z, -1f, 1f) * speed01);
    }

    private static float FlatDist(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f; return Vector3.Distance(a, b);
    }

    private float GroundY(Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 60f, ~(1 << 9)))
            return hit.point.y;
        if (Terrain.activeTerrain != null)
            return Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;
        return pos.y;
    }

    private IEnumerator LeaveRoutine()
    {
        leaving = true;
        if (leaveVFXPrefab != null) Instantiate(leaveVFXPrefab, transform.position, Quaternion.identity);
        yield return null;
        Destroy(gameObject);
    }
}
