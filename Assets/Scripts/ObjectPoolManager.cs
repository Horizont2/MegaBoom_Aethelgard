using UnityEngine;
using System.Collections.Generic;

public class ObjectPoolManager : MonoBehaviour
{
    public static ObjectPoolManager Instance;

    private Dictionary<string, Queue<GameObject>> poolDictionary = new Dictionary<string, Queue<GameObject>>();

    // НОВЕ: Словник для зберігання папок-контейнерів
    private Dictionary<string, Transform> poolParents = new Dictionary<string, Transform>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public GameObject SpawnFromPool(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null; // Захист від NullReference

        string poolKey = prefab.name;

        if (!poolDictionary.ContainsKey(poolKey))
        {
            poolDictionary.Add(poolKey, new Queue<GameObject>());

            // ААА-Організація: Створюємо контейнер для цього типу об'єктів
            GameObject parentObj = new GameObject(poolKey + "_Pool");
            parentObj.transform.SetParent(this.transform);
            poolParents.Add(poolKey, parentObj.transform);
        }

        GameObject objectToSpawn = null;

        // Шукаємо вільний об'єкт у пулі
        if (poolDictionary[poolKey].Count > 0)
        {
            objectToSpawn = poolDictionary[poolKey].Dequeue();
        }
        else
        {
            // Якщо пул порожній, створюємо новий
            objectToSpawn = Instantiate(prefab);
            objectToSpawn.name = prefab.name; // Зберігаємо чисте ім'я без "(Clone)"

            // Ховаємо об'єкт у відповідну папку в Ієрархії
            objectToSpawn.transform.SetParent(poolParents[poolKey]);
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

            // Якщо папки ще немає (наприклад, об'єкт створили поза пулом, але повертають в нього)
            if (!poolParents.ContainsKey(poolKey))
            {
                GameObject parentObj = new GameObject(poolKey + "_Pool");
                parentObj.transform.SetParent(this.transform);
                poolParents.Add(poolKey, parentObj.transform);
            }
        }

        // Переконуємося, що об'єкт лежить у своїй папці при поверненні
        if (poolParents.ContainsKey(poolKey))
        {
            obj.transform.SetParent(poolParents[poolKey]);
        }

        poolDictionary[poolKey].Enqueue(obj);
    }
}