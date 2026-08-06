using UnityEngine;

public class POISpawner : MonoBehaviour
{
    [Header("POI Settings")]
    [Tooltip("Префаби локацій (Табір, Руїни і т.д.)")]
    public GameObject[] locationPrefabs;

    [Tooltip("Скільки локацій створити на мапі")]
    public int amountToSpawn = 15;

    [Tooltip("Розмір зони спавну (наприклад, 200 означає від -100 до +100)")]
    public float mapSize = 250f;

    [Header("Placement Rules")]
    [Tooltip("Максимальний кут нахилу землі (в градусах), де може з'явитися локація")]
    public float maxSlopeAngle = 10f;

    private void Start()
    {
        SpawnLocations();
    }

    private void SpawnLocations()
    {
        if (locationPrefabs == null || locationPrefabs.Length == 0 || Terrain.activeTerrain == null) return;

        int spawnedCount = 0;
        int maxAttempts = 2000;
        int currentAttempt = 0;

        while (spawnedCount < amountToSpawn && currentAttempt < maxAttempts)
        {
            currentAttempt++;

            float randomX = Random.Range(-mapSize / 2f, mapSize / 2f);
            float randomZ = Random.Range(-mapSize / 2f, mapSize / 2f);

            Vector3 skyPos = new Vector3(randomX, 1000f, randomZ);

            if (Physics.Raycast(skyPos, Vector3.down, out RaycastHit hit, 2000f))
            {
                float slopeAngle = Vector3.Angle(Vector3.up, hit.normal);

                if (slopeAngle <= maxSlopeAngle)
                {
                    GameObject prefab = locationPrefabs[Random.Range(0, locationPrefabs.Length)];
                    Quaternion randomRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

                    // Спавнимо об'єкт
                    GameObject instance = Instantiate(prefab, hit.point, randomRotation, transform);

                    // ФІКС: Шукаємо найнижчу точку моделі (Mesh)
                    float lowestY = float.MaxValue;
                    bool hasRenderers = false;

                    foreach (var rend in instance.GetComponentsInChildren<Renderer>(false))
                    {
                        if (!rend.enabled || rend is ParticleSystemRenderer) continue;
                        lowestY = Mathf.Min(lowestY, rend.bounds.min.y);
                        hasRenderers = true;
                    }

                    // Притискаємо об'єкт до землі, компенсуючи зміщення Pivot'а
                    if (hasRenderers)
                    {
                        float delta = hit.point.y - lowestY;
                        instance.transform.position += new Vector3(0f, delta, 0f);
                    }

                    spawnedCount++;
                }
            }
        }

        if (spawnedCount < amountToSpawn)
        {
            Debug.LogWarning($"Змогли заспавнити лише {spawnedCount} локацій з {amountToSpawn}. Не вистачило рівного місця!");
        }
    }
}