using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class DistanceOptimizer : MonoBehaviour
{
    public static DistanceOptimizer Instance;

    [Header("Settings")]
    public Transform player;

    [Tooltip("На якій відстані об'єкти З'ЯВЛЯЮТЬСЯ")]
    public float enableDistance = 100f;

    [Tooltip("На якій відстані об'єкти ЗНИКАЮТЬ (Має бути більшим за Enable, щоб уникнути спаму)")]
    public float disableDistance = 115f;

    [Tooltip("Частота перевірки. 0.2 = швидке оновлення 5 разів на секунду")]
    public float checkInterval = 0.2f;

    [Tooltip("Кількість об'єктів за кадр. 500 - золота середина")]
    public int checksPerFrame = 500;

    private List<OptimizedObject> managedObjects = new List<OptimizedObject>();

    // Кешовані квадрати дистанцій для надшвидкої математики
    private float sqrEnableDist;
    private float sqrDisableDist;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // Кешуємо квадрати одразу, щоб не множити їх у циклі
        sqrEnableDist = enableDistance * enableDistance;
        sqrDisableDist = disableDistance * disableDistance;
    }

    private void Start()
    {
        FindPlayerIfNeeded();
        StartCoroutine(OptimizationRoutine());
    }

    private void FindPlayerIfNeeded()
    {
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
    }

    public void RegisterObject(OptimizedObject obj)
    {
        managedObjects.Add(obj);

        // Відключаємо важкий InitialCheck тут, бо корутина сама все підхопить
        // і розподілить навантаження рівномірно (без лагів при генерації)
    }

    public void UnregisterObject(OptimizedObject obj)
    {
        // ВАЖЛИВО ДЛЯ ШВИДКОСТІ:
        // Замість .Remove() який зсуває весь масив (що дуже повільно для 10k об'єктів),
        // ми просто зануляємо його. Корутина сама його потім прибере без лагів.
        int index = managedObjects.IndexOf(obj);
        if (index != -1)
        {
            managedObjects[index] = null;
        }
    }

    private IEnumerator OptimizationRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(checkInterval);

        while (true)
        {
            if (player == null || managedObjects.Count == 0)
            {
                yield return wait;
                continue;
            }

            Vector3 playerPos = player.position;
            int count = 0;

            for (int i = managedObjects.Count - 1; i >= 0; i--)
            {
                OptimizedObject obj = managedObjects[i];

                // Ліниве (швидке) видалення знищених/відреєстрованих об'єктів
                if (obj == null || obj.targetObject == null)
                {
                    managedObjects.RemoveAt(i);
                    continue;
                }

                float distSqr = (obj.transform.position - playerPos).sqrMagnitude;
                bool isActive = obj.targetObject.activeSelf;

                // --- НОВА ЛОГІКА З ГІСТЕРЕЗИСОМ ---
                if (isActive)
                {
                    // Якщо об'єкт увімкнений, вимикаємо його ТІЛЬКИ якщо він відійшов за disableDistance
                    if (distSqr > sqrDisableDist)
                    {
                        obj.targetObject.SetActive(false);
                    }
                }
                else
                {
                    // Якщо об'єкт вимкнений, вмикаємо його ТІЛЬКИ якщо він підійшов ближче enableDistance
                    if (distSqr <= sqrEnableDist)
                    {
                        obj.targetObject.SetActive(true);
                    }
                }

                count++;

                if (count >= checksPerFrame)
                {
                    count = 0;
                    yield return null; // Чекаємо наступного кадру, щоб не фризити гру

                    if (player != null) playerPos = player.position; // Оновлюємо позицію після кадру
                }
            }

            yield return wait;
        }
    }
}