using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GrenadeLogic : MonoBehaviour
{
    [Header("Explosion Settings")]
    public float fallbackDelay = 3f;
    public float explosionRadius = 6f;
    public float damage = 200f;

    [Header("Effects & Loot")]
    public GameObject explosionEffect;
    public GameObject crystalPrefab;

    [Header("Game Feel (Juice)")]
    public float baseHitStopDuration = 0.05f;
    public float maxHitStopDuration = 0.15f;
    public float baseShakeMagnitude = 0.2f;
    public float shakeMultiplier = 0.05f;

    private float countdown;
    private bool hasExploded = false;
    private CameraFollow mainCameraScript;
    private MeshRenderer meshRenderer;

    private static readonly Collider[] s_explosionBuffer = new Collider[64];
    // Dedupe set so an enemy with several child colliders only takes one hit
    // per explosion.
    private static readonly HashSet<IDamageable> s_hitThisBlast = new HashSet<IDamageable>();

    private void Start()
    {
        countdown = fallbackDelay;
        if (Camera.main != null) mainCameraScript = Camera.main.GetComponent<CameraFollow>();
        meshRenderer = GetComponent<MeshRenderer>();

        // Clear any TrailRenderer segments left over from the previous
        // pooled use — the pool re-uses grenade instances and the trail
        // keeps positions from the last throw. Without this the player
        // sees a long streak from wherever the last grenade landed to
        // the new spawn point (especially obvious when aiming at a boss).
        var trails = GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
        {
            if (trails[i] == null) continue;
            trails[i].Clear();
            trails[i].emitting = true;
        }
    }

    private void OnEnable()
    {
        // Second-clear on pool reactivation — Start() only fires once, but
        // OnEnable fires every time the pool re-uses this instance.
        var trails = GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
        {
            if (trails[i] == null) continue;
            trails[i].Clear();
        }
    }

    private void Update()
    {
        if (hasExploded) return;

        countdown -= Time.deltaTime;
        if (countdown <= 0f) Explode();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (hasExploded) return;
        if (!collision.gameObject.CompareTag("Player"))
        {
            Explode();
        }
    }

    private void Explode()
    {
        hasExploded = true;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Explosion);

        if (explosionEffect != null) ObjectPoolManager.Instance.SpawnFromPool(explosionEffect, transform.position, Quaternion.identity);

        int colliderCount = Physics.OverlapSphereNonAlloc(transform.position, explosionRadius, s_explosionBuffer);
        int enemyCount = 0;

        for (int i = 0; i < colliderCount; i++)
        {
            if (s_explosionBuffer[i].CompareTag("Enemy")) enemyCount++;
        }

        int multiplier = 1;
        if (enemyCount >= 20) multiplier = 4;
        else if (enemyCount >= 10) multiplier = 2;

        if (enemyCount > 0)
        {
            float currentHitStop = Mathf.Clamp(baseHitStopDuration + (enemyCount * 0.005f), baseHitStopDuration, maxHitStopDuration);
            float currentShake = baseShakeMagnitude + (enemyCount * shakeMultiplier);

            if (mainCameraScript != null) mainCameraScript.TriggerShake(0.3f, currentShake);
            StartCoroutine(HitStopRoutine(currentHitStop));
        }

        s_hitThisBlast.Clear();
        for (int i = 0; i < colliderCount; i++)
        {
            Collider nearbyObject = s_explosionBuffer[i];
            if (nearbyObject == null) continue;

            // Resolve the damageable on the collider OR its parent. Archers and
            // bosses keep their EnemyAI on the parent with CHILD colliders, so the
            // old TryGetComponent (collider's own object only) never found them —
            // that's why grenades did no damage to archers.
            IDamageable damageable = nearbyObject.GetComponentInParent<IDamageable>();
            if (damageable == null) continue;

            // Classify by COMPONENT TYPE, not tag. Archers/bosses put their AI on
            // the parent and their child colliders are often untagged, so the old
            // tag gate skipped them entirely (that's why grenades did no damage to
            // archers — melee works because it has no tag gate). Any EnemyAI /
            // boss is an enemy; the PlayerController is the player.
            Component dmgComp = damageable as Component;
            bool isPlayer = dmgComp is PlayerController;
            bool isEnemy = !isPlayer && (dmgComp is EnemyAI || dmgComp is TutorialBossAI);
            if (!isEnemy && !isPlayer) continue;

            // One enemy with several child colliders must only take one hit.
            if (!s_hitThisBlast.Add(damageable)) continue;

            {
                Vector3 pushDir = (nearbyObject.transform.position - transform.position).normalized;
                pushDir.y = 0;

                float finalDamage = isPlayer ? 20f : damage;

                DamageInfo info = new DamageInfo
                {
                    Amount = finalDamage,
                    IsCritical = false,
                    PushDirection = pushDir,
                    KnockbackForce = isPlayer ? 5f : 15f,
                    StunDuration = isPlayer ? 0.2f : 1.5f,
                    HitPoint = nearbyObject.ClosestPoint(transform.position)
                };

                damageable.TakeDamage(info);

                if (!isPlayer && crystalPrefab != null && ObjectPoolManager.Instance != null)
                {
                    for (int m = 0; m < multiplier - 1; m++)
                    {
                        Vector3 offset = new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f));
                        ObjectPoolManager.Instance.SpawnFromPool(crystalPrefab, nearbyObject.transform.position + offset, Quaternion.identity);
                    }
                }
            }
        }

        if (meshRenderer != null) meshRenderer.enabled = false;
        GetComponent<Collider>().enabled = false;
        Destroy(gameObject, maxHitStopDuration + 0.1f);
    }

    private IEnumerator HitStopRoutine(float duration)
    {
        // Don't hit-stop over a slow-mo that a higher-priority system owns
        // (grenade-aim 0.25x, perfect-dodge bullet-time, a level-up menu at 0,
        // or the pause menu) — forcing 0.05 then snapping back to 1 broke those.
        if (LevelUpManager.IsMenuOpen || Time.timeScale < 0.9f) yield break;

        Time.timeScale = 0.05f;
        // Failsafe: a scene change / object destroy during the realtime wait
        // must not strand the game in slow-mo.
        CinematicTimeGuard.Arm(duration + 0.5f);
        yield return new WaitForSecondsRealtime(duration);
        // Only restore if we're still the one holding time (nothing else took
        // over during the wait).
        if (Time.timeScale <= 0.06f) Time.timeScale = 1f;
    }
}