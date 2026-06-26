using UnityEngine;

public class TreeVFX : MonoBehaviour
{
    [Header("VFX Prefabs (Spawned on action)")]
    public GameObject hitVFXPrefab;
    public GameObject fallDustPrefab;

    public void PlayHitEffect()
    {
        if (hitVFXPrefab != null)
        {
            Collider col = GetComponent<Collider>();
            Vector3 spawnPos = transform.position + Vector3.up * 1f;
            if (col != null)
            {
                spawnPos = new Vector3(col.bounds.center.x, col.bounds.min.y + 1f, col.bounds.center.z);
            }

            GameObject fx = Instantiate(hitVFXPrefab, spawnPos, Quaternion.identity);

            // ФІКС: Вмикаємо об'єкт, бо префаб міг бути деактивованим для оптимізації
            fx.SetActive(true);

            ParticleSystem ps = fx.GetComponentInChildren<ParticleSystem>();
            if (ps != null) ps.Play();

            Destroy(fx, 2f);
        }
    }

    public void PlayFallEffect(Vector3 rootPosition)
    {
        if (fallDustPrefab != null)
        {
            GameObject fx = Instantiate(fallDustPrefab, rootPosition + Vector3.up * 0.2f, Quaternion.identity);

            fx.SetActive(true); // Вмикаємо пил

            ParticleSystem ps = fx.GetComponentInChildren<ParticleSystem>();
            if (ps != null) ps.Play();

            Destroy(fx, 4f);
        }
    }
}