using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class DistanceOptimizer : MonoBehaviour
{
    public static DistanceOptimizer Instance { get; private set; }

    [Header("Settings")]
    public Transform player;

    [Tooltip("Дистанція, на якій об'єкти вмикаються")]
    public float enableDistance = 100f;

    [Tooltip("Дистанція, на якій об'єкти вимикаються (Гістерезис)")]
    public float disableDistance = 115f;

    [Tooltip("Як часто оновлювати (в секундах)")]
    public float checkInterval = 0.2f;

    [Tooltip("Скільки об'єктів перевіряти за один кадр")]
    public int checksPerFrame = 500;

    private List<OptimizedObject> managedObjects = new List<OptimizedObject>();
    private float sqrEnableDist;
    private float sqrDisableDist;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

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
        if (!managedObjects.Contains(obj))
            managedObjects.Add(obj);
    }

    public void UnregisterObject(OptimizedObject obj)
    {
        int index = managedObjects.IndexOf(obj);
        if (index != -1)
        {
            // O(1) видалення зі списку для оптимізації CPU
            managedObjects[index] = managedObjects[managedObjects.Count - 1];
            managedObjects.RemoveAt(managedObjects.Count - 1);
        }
    }

    private IEnumerator OptimizationRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(checkInterval);

        while (true)
        {
            if (player == null || managedObjects.Count == 0)
            {
                if (player == null) FindPlayerIfNeeded();
                yield return wait;
                continue;
            }

            Vector3 playerPos = player.position;
            int processedThisFrame = 0;

            for (int i = managedObjects.Count - 1; i >= 0; i--)
            {
                OptimizedObject obj = managedObjects[i];

                if (obj == null)
                {
                    managedObjects[i] = managedObjects[managedObjects.Count - 1];
                    managedObjects.RemoveAt(managedObjects.Count - 1);
                    continue;
                }

                float distSqr = (obj.transform.position - playerPos).sqrMagnitude;

                if (obj.isCurrentlyVisible)
                {
                    if (distSqr > sqrDisableDist) obj.SetVisibility(false);
                }
                else
                {
                    if (distSqr <= sqrEnableDist) obj.SetVisibility(true);
                }

                processedThisFrame++;

                if (processedThisFrame >= checksPerFrame)
                {
                    processedThisFrame = 0;
                    yield return null;
                    if (player != null) playerPos = player.position;
                }
            }

            yield return wait;
        }
    }
}