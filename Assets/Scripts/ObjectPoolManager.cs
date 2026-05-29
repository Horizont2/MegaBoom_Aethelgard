using UnityEngine;
using System.Collections.Generic;

public class ObjectPoolManager : MonoBehaviour
{
    public static ObjectPoolManager Instance;

    private Dictionary<string, Queue<GameObject>> poolDictionary = new Dictionary<string, Queue<GameObject>>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public GameObject SpawnFromPool(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        string poolKey = prefab.name;

        if (!poolDictionary.ContainsKey(poolKey))
        {
            poolDictionary.Add(poolKey, new Queue<GameObject>());
        }

        GameObject objectToSpawn = null;

        // ЎукаЇмо в≥льний об'Їкт у пул≥
        if (poolDictionary[poolKey].Count > 0)
        {
            objectToSpawn = poolDictionary[poolKey].Dequeue();
        }
        else
        {
            // якщо пул порожн≥й, створюЇмо новий
            objectToSpawn = Instantiate(prefab);
            objectToSpawn.name = prefab.name; // «бер≥гаЇмо чисте ≥м'€ без "(Clone)"
        }

        objectToSpawn.transform.position = position;
        objectToSpawn.transform.rotation = rotation;
        objectToSpawn.SetActive(true);

        return objectToSpawn;
    }

    public void ReturnToPool(GameObject obj)
    {
        obj.SetActive(false);
        string poolKey = obj.name;

        if (!poolDictionary.ContainsKey(poolKey))
        {
            poolDictionary.Add(poolKey, new Queue<GameObject>());
        }

        poolDictionary[poolKey].Enqueue(obj);
    }
}