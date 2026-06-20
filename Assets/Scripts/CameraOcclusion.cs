using UnityEngine;
using System.Collections.Generic;

public class CameraOcclusion : MonoBehaviour
{
    [Header("Target & Scanning")]
    public Transform playerTarget;
    public LayerMask foliageLayer;
    public float raycastRadius = 0.1f;

    [Header("Fade Settings")]
    [Range(0f, 1f)] public float fadeAlpha = 0.25f;
    public float fadeSpeed = 4f;

    private List<FadingObject> currentlyFaded = new List<FadingObject>();
    private List<FadingObject> hitsThisFrame = new List<FadingObject>();

    // СУПЕР-ОПТИМІЗАЦІЯ: Кешуємо всі дерева, щоб не викликати GetComponent при русі камери
    private Dictionary<Collider, FadingObject> faderCache = new Dictionary<Collider, FadingObject>();

    private void Start()
    {
        if (playerTarget == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerTarget = player.transform;
        }
    }

    private void Update()
    {
        if (playerTarget == null || Time.frameCount < 10) return; // Чекаємо кілька кадрів після завантаження

        hitsThisFrame.Clear();

        Vector3 startPos = transform.position;
        Vector3 endPos = playerTarget.position + Vector3.up * 1.5f;

        Vector3 dir = (endPos - startPos).normalized;
        float dist = Vector3.Distance(startPos, endPos);
        float checkDistance = Mathf.Max(0f, dist - 1.5f);

        RaycastHit[] hits = Physics.SphereCastAll(startPos, raycastRadius, dir, checkDistance, foliageLayer);

        foreach (RaycastHit hit in hits)
        {
            // Швидкі відсіювання
            if (hit.collider is TerrainCollider) continue;

            FadingObject fader = null;

            // Перевіряємо кеш (Миттєво)
            if (faderCache.TryGetValue(hit.collider, out FadingObject cachedFader))
            {
                fader = cachedFader;
            }
            else
            {
                // Якщо об'єкта немає в кеші - шукаємо один раз і запам'ятовуємо назавжди
                fader = hit.collider.GetComponentInParent<FadingObject>();
                if (fader == null && hit.collider.GetComponentInParent<MeshRenderer>() != null)
                {
                    fader = hit.collider.gameObject.AddComponent<FadingObject>();
                    fader.Initialize(fadeAlpha, fadeSpeed);
                }

                faderCache[hit.collider] = fader; // Записуємо в кеш
            }

            if (fader == null) continue;

            hitsThisFrame.Add(fader);
            fader.FadeOut();

            if (!currentlyFaded.Contains(fader)) currentlyFaded.Add(fader);
        }

        // Повертаємо непрозорість тим, на кого більше не дивимось
        for (int i = currentlyFaded.Count - 1; i >= 0; i--)
        {
            FadingObject fader = currentlyFaded[i];
            if (!hitsThisFrame.Contains(fader))
            {
                if (fader != null) fader.FadeIn();
                currentlyFaded.RemoveAt(i);
            }
        }
    }
}