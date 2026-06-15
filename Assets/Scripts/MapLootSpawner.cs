using UnityEngine;
using System.Collections;

public class MapLootSpawner : MonoBehaviour
{
    [Header("Loot Settings")]
    public GameObject xpCrystalPrefab;
    public int amountToSpawn = 150;
    public float scatterRadius = 150f;

    [Header("AAA Placement Settings")]
    [Tooltip("Шар землі, на який можна класти лут")]
    public LayerMask groundLayer;
    [Tooltip("Шари перешкод (дерева, каміння), щоб лут не спавнився в них")]
    public LayerMask obstacleLayer;
    [Tooltip("Мінімальна відстань між кристалами/перешкодами")]
    public float minClearanceRadius = 1.5f;

    [Header("Performance")]
    [Tooltip("Скільки об'єктів створювати за один кадр (щоб не було лагів)")]
    public int spawnsPerFrame = 5;

    private void Start()
    {
        // За замовчуванням беремо базові шари, якщо забув налаштувати
        if (groundLayer == 0) groundLayer = LayerMask.GetMask("Default", "Terrain", "Ground");

        StartCoroutine(SpawnLootAsync());
    }

    private IEnumerator SpawnLootAsync()
    {
        if (xpCrystalPrefab == null) yield break;

        // Чекаємо секунду, щоб ландшафт і дерева 100% встигли згенеруватися
        yield return new WaitForSeconds(1f);

        int successfullySpawned = 0;
        int currentFrameSpawns = 0;
        int maxAttemptsPerItem = 10; // Захист від вічного циклу

        for (int i = 0; i < amountToSpawn; i++)
        {
            for (int attempt = 0; attempt < maxAttemptsPerItem; attempt++)
            {
                // Шукаємо випадкову точку на площині
                Vector2 randomPoint = Random.insideUnitCircle * scatterRadius;
                // Пускаємо промінь високо з неба вниз
                Vector3 rayStart = transform.position + new Vector3(randomPoint.x, 500f, randomPoint.y);

                if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 1000f, groundLayer))
                {
                    // 1. Захист від стрімких скель (не спавнимо на схилах більше 40 градусів)
                    if (Vector3.Angle(Vector3.up, hit.normal) > 40f) continue;

                    Vector3 spawnPos = hit.point + Vector3.up * 0.4f;

                    // 2. Захист від накладання: перевіряємо, чи немає поруч дерева або іншого луту
                    if (!Physics.CheckSphere(spawnPos, minClearanceRadius, obstacleLayer))
                    {
                        // Рандомізуємо поворот по осі Y, щоб лут не стояв "як солдати"
                        Quaternion randomRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                        GameObject loot = Instantiate(xpCrystalPrefab, spawnPos, randomRot);

                        // Ховаємо лут у батьківський об'єкт для чистоти Ієрархії
                        loot.transform.SetParent(transform);

                        successfullySpawned++;
                        break; // Успішно заспавнили, переходимо до наступного
                    }
                }
            }

            // Розподіляємо навантаження на процесор (Time-Slicing)
            currentFrameSpawns++;
            if (currentFrameSpawns >= spawnsPerFrame)
            {
                currentFrameSpawns = 0;
                yield return null; // Передаємо керування рушію до наступного кадру
            }
        }

        Debug.Log($"[MapLootSpawner] Успішно розкидано луту: {successfullySpawned}/{amountToSpawn} (Без лагів)");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, scatterRadius);
    }
}