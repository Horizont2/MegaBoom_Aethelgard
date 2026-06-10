using UnityEngine;
using System.Collections;

public class ResourceNode : MonoBehaviour, IDamageable // НОВЕ: Інтерфейс
{
    public enum NodeType { Tree, Rock, Barrel }

    [Header("Type Settings")]
    public NodeType nodeType = NodeType.Tree;
    public float minHealth = 50f;
    public float maxHealth = 200f;

    [Header("Drops")]
    public GameObject dropPrefab;
    public int minDrops = 2;
    public int maxDrops = 5;

    [Header("Effects")]
    public ParticleSystem hitEffect;
    public GameObject stumpPrefab;

    private float currentHealth;
    private float actualMaxHealth;
    private Vector3 originalScale;
    private bool isDead = false;

    private void Start()
    {
        actualMaxHealth = Random.Range(minHealth, maxHealth);
        currentHealth = actualMaxHealth;
        originalScale = transform.localScale;
    }

    // ААА-архітектура: Приймаємо структуру замість простої цифри
    public void TakeDamage(DamageInfo info)
    {
        if (isDead) return;

        currentHealth -= info.Amount;

        // Випускаємо пил/іскри при кожному ударі
        if (hitEffect != null) hitEffect.Play();

        StopAllCoroutines();

        if (currentHealth <= 0)
        {
            isDead = true;
            StartCoroutine(DeathRoutine());
        }
        else
        {
            // РОЗУМНА АНІМАЦІЯ ЗАЛЕЖНО ВІД ТИПУ
            if (nodeType == NodeType.Rock)
            {
                // Камінь фізично зменшується
                float healthPercent = currentHealth / maxHealth;
                Vector3 targetScale = originalScale * Mathf.Max(0.4f, healthPercent);
                StartCoroutine(SquishRoutine(targetScale));
            }
            else
            {
                // Дерева і бочки хитаються від удару
                StartCoroutine(WobbleRoutine());
            }
        }
    }

    private IEnumerator SquishRoutine(Vector3 targetScale)
    {
        float t = 0;
        Vector3 squishScale = new Vector3(targetScale.x * 1.1f, targetScale.y * 0.9f, targetScale.z * 1.1f);
        while (t < 1) { t += Time.deltaTime * 15f; transform.localScale = Vector3.Lerp(transform.localScale, squishScale, t); yield return null; }
        t = 0;
        while (t < 1) { t += Time.deltaTime * 10f; transform.localScale = Vector3.Lerp(squishScale, targetScale, t); yield return null; }
        transform.localScale = targetScale;
    }

    private IEnumerator WobbleRoutine()
    {
        float t = 0;
        Vector3 squishScale = new Vector3(originalScale.x * 1.1f, originalScale.y * 0.9f, originalScale.z * 1.1f);
        while (t < 1) { t += Time.deltaTime * 15f; transform.localScale = Vector3.Lerp(originalScale, squishScale, t); yield return null; }
        t = 0;
        while (t < 1) { t += Time.deltaTime * 10f; transform.localScale = Vector3.Lerp(squishScale, originalScale, t); yield return null; }
        transform.localScale = originalScale;
    }

    private IEnumerator DeathRoutine()
    {
        int dropCount = Random.Range(minDrops, maxDrops + 1);
        for (int i = 0; i < dropCount; i++)
        {
            if (dropPrefab != null) Instantiate(dropPrefab, transform.position + Vector3.up * 1.5f, Quaternion.identity);
        }

        if (nodeType == NodeType.Tree)
        {
            Vector3 pivotPoint = transform.position;
            Collider col = GetComponentInChildren<Collider>();
            if (col != null) pivotPoint.y = col.bounds.min.y;

            float fallDuration = 0.5f;
            float fallSpeed = 90f / fallDuration;
            float t = 0;

            while (t < fallDuration)
            {
                t += Time.deltaTime;
                transform.RotateAround(pivotPoint, transform.right, fallSpeed * Time.deltaTime);
                yield return null;
            }

            if (hitEffect != null) hitEffect.Play();
            if (stumpPrefab != null) Instantiate(stumpPrefab, pivotPoint, transform.rotation);

            yield return new WaitForSeconds(0.5f);
            Destroy(gameObject);
        }
        else
        {
            if (hitEffect != null) hitEffect.Play();

            if (GetComponentInChildren<MeshRenderer>() != null) GetComponentInChildren<MeshRenderer>().enabled = false;
            if (GetComponent<Collider>() != null) GetComponent<Collider>().enabled = false;

            yield return new WaitForSeconds(1.5f);
            Destroy(gameObject);
        }
    }
}