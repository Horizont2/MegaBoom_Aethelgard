using UnityEngine;
using System.Collections;

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

    private void Start()
    {
        countdown = fallbackDelay;
        if (Camera.main != null) mainCameraScript = Camera.main.GetComponent<CameraFollow>();
        meshRenderer = GetComponent<MeshRenderer>();
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

        Collider[] colliders = Physics.OverlapSphere(transform.position, explosionRadius);
        int enemyCount = 0;

        foreach (Collider nearbyObject in colliders)
        {
            if (nearbyObject.CompareTag("Enemy")) enemyCount++;
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

        foreach (Collider nearbyObject in colliders)
        {
            if (nearbyObject.TryGetComponent(out IDamageable damageable))
            {
                // ФІКС: Граната наносить шкоду ТІЛЬКИ ворогам та гравцю (ігнорує дерева/ресурси)
                if (!nearbyObject.CompareTag("Enemy") && !nearbyObject.CompareTag("Player")) continue;

                Vector3 pushDir = (nearbyObject.transform.position - transform.position).normalized;
                pushDir.y = 0;

                bool isPlayer = nearbyObject.CompareTag("Player");
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
                    for (int i = 0; i < multiplier - 1; i++)
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
        Time.timeScale = 0.05f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1f;
    }
}