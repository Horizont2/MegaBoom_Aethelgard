using UnityEngine;
using System.Collections.Generic;

public class CameraOcclusion : MonoBehaviour
{
    [Header("Target & Scanning")]
    public Transform playerTarget;
    public LayerMask foliageLayer;
    [Tooltip("Зменшений радіус, щоб не чіпляти зайве")]
    public float raycastRadius = 0.5f;

    [Header("Fade Settings")]
    [Range(0f, 1f)] public float fadeAlpha = 0.25f;
    public float fadeSpeed = 4f;

    private List<FadingObject> currentlyFaded = new List<FadingObject>();
    private List<FadingObject> hitsThisFrame = new List<FadingObject>();

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
        if (playerTarget == null) return;

        hitsThisFrame.Clear();

        Vector3 startPos = transform.position;
        Vector3 endPos = playerTarget.position + Vector3.up * 1.5f;

        Vector3 dir = (endPos - startPos).normalized;
        float dist = Vector3.Distance(startPos, endPos);

        RaycastHit[] hits = Physics.SphereCastAll(startPos, raycastRadius, dir, dist - 0.5f, foliageLayer);

        foreach (RaycastHit hit in hits)
        {
            // КРИТИЧНИЙ ФІКС FPS: Жорстко ігноруємо землю та траву!
            if (hit.collider.GetComponent<Terrain>() != null) continue;
            if (hit.collider.name.ToLower().Contains("grass")) continue;

            FadingObject fader = hit.collider.GetComponentInParent<FadingObject>();

            if (fader == null)
            {
                // Додаємо скрипт прозорості тільки якщо об'єкт реально має текстуру (MeshRenderer)
                if (hit.collider.GetComponentInParent<MeshRenderer>() != null)
                {
                    fader = hit.collider.gameObject.AddComponent<FadingObject>();
                    fader.Initialize(fadeAlpha, fadeSpeed);
                }
                else continue;
            }

            hitsThisFrame.Add(fader);
            fader.FadeOut();

            if (!currentlyFaded.Contains(fader)) currentlyFaded.Add(fader);
        }

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