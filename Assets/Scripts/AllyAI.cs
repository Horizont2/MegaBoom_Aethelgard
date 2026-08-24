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
    [Tooltip("The ally CAN die — it isn't invincible. Kept modest so a freed captive is a helper, not a juggernaut.")]
    public float maxHealth = 60f;
    [Tooltip("Chip damage per second the ally takes for EACH enemy in melee range — so a lone ally worn down by a crowd eventually falls.")]
    public float meleeRetaliationDPS = 5f;
    public GameObject deathVFXPrefab;
    private float currentHealth;
    private bool dead;

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
            else { MoveToward(ep); SetMoving(true); }
        }
        else
        {
            // No enemy near — stick close to the player.
            if (FlatDist(transform.position, player.position) > followDistance)
            {
                MoveToward(player.position);
                FaceToward(player.position);
                SetMoving(true);
            }
            else SetMoving(false);
        }
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

    private void MoveToward(Vector3 dest)
    {
        Vector3 dir = dest - transform.position; dir.y = 0f;
        float d = dir.magnitude;
        if (d < 0.001f) return;
        dir /= d;
        // Ease speed up/down instead of snapping to full velocity instantly.
        _curSpeed = Mathf.MoveTowards(_curSpeed, moveSpeed, moveSpeed * 3f * Time.deltaTime);
        Vector3 next = transform.position + dir * _curSpeed * Time.deltaTime;
        next.y = GroundY(next);
        transform.position = next;
    }

    private void FaceToward(Vector3 p)
    {
        Vector3 dir = p - transform.position; dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 12f * Time.deltaTime);
    }

    private void SetMoving(bool m)
    {
        if (animator == null) return;
        if (!m) _curSpeed = Mathf.MoveTowards(_curSpeed, 0f, moveSpeed * 4f * Time.deltaTime);
        // Drive BOTH conventions safely: a plain "isMoving" bool AND the
        // Speed/MoveX/MoveZ blend-tree params HeroAnimator uses. Set…Safe no-ops
        // when a param is absent, so nothing warns whichever controller is on it.
        animator.SetBoolSafe("isMoving", m);
        animator.SetBoolSafe("IsGrounded", true);
        animator.SetFloatSafe("Speed", m ? _curSpeed : 0f);
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
